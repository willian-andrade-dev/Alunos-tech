using System.Data;
using AlunosTech.Chamados;
using Npgsql;
using NpgsqlTypes;

public sealed class ChamadosPostgres(NpgsqlDataSource banco)
{
    public async Task CriarAsync(Chamado chamado, CancellationToken cancelamento)
    {
        await using var conexao = await banco.OpenConnectionAsync(cancelamento);
        await using var transacao = await conexao.BeginTransactionAsync(cancelamento);
        await using var comando = new NpgsqlCommand("""
            INSERT INTO public.Chamado
                (Id, SolicitanteId, Titulo, Categoria, Descricao, Status, CriadoEm)
            VALUES ($1, $2, $3, $4, $5, 0, $6)
            """, conexao, transacao);
        comando.Parameters.AddWithValue(chamado.Id);
        comando.Parameters.AddWithValue(chamado.SolicitanteId);
        comando.Parameters.AddWithValue(chamado.Titulo);
        comando.Parameters.AddWithValue(chamado.Categoria);
        comando.Parameters.AddWithValue(chamado.Descricao);
        comando.Parameters.AddWithValue(chamado.Historico[0].Data);
        await comando.ExecuteNonQueryAsync(cancelamento);
        await GravarEventoAsync(conexao, transacao, chamado.Id, chamado.Historico[0], cancelamento);
        await transacao.CommitAsync(cancelamento);
    }

    public async Task<IReadOnlyList<ResumoChamado>> ListarAsync(Guid usuarioId,
        bool tecnico, StatusChamado? status, CancellationToken cancelamento)
    {
        await using var comando = banco.CreateCommand("""
            SELECT Id, Titulo, Categoria, Status, TecnicoId
            FROM public.Chamado
            WHERE ($1 OR SolicitanteId = $2)
              AND ($3::smallint IS NULL OR Status = $3)
            ORDER BY CriadoEm DESC, Id
            """);
        comando.Parameters.AddWithValue(tecnico);
        comando.Parameters.AddWithValue(usuarioId);
        comando.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Smallint,
            Value = status.HasValue ? (short)status.Value : DBNull.Value });
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento);
        var lista = new List<ResumoChamado>();
        while (await leitor.ReadAsync(cancelamento))
            lista.Add(new(leitor.GetGuid(0), leitor.GetString(1), leitor.GetString(2),
                (StatusChamado)leitor.GetInt16(3), leitor.IsDBNull(4) ? null : leitor.GetGuid(4)));
        return lista;
    }

    public async Task<Chamado?> BuscarAsync(Guid id, CancellationToken cancelamento)
    {
        await using var conexao = await banco.OpenConnectionAsync(cancelamento);
        // As duas consultas precisam observar o mesmo estado do chamado.
        await using var transacao = await conexao.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancelamento);
        var chamado = await CarregarAsync(conexao, transacao, id, false, cancelamento);
        await transacao.CommitAsync(cancelamento);
        return chamado;
    }

    public async Task<Chamado?> AtualizarAsync(Guid id,
        Action<Chamado> alterar, CancellationToken cancelamento)
    {
        await using var conexao = await banco.OpenConnectionAsync(cancelamento);
        await using var transacao = await conexao.BeginTransactionAsync(cancelamento);
        // O bloqueio vale entre processos e instâncias diferentes da API.
        var chamado = await CarregarAsync(conexao, transacao, id, true, cancelamento);
        if (chamado is null)
            return null;

        alterar(chamado);
        await using var comando = new NpgsqlCommand("""
            UPDATE public.Chamado SET Status = $1, TecnicoId = $2 WHERE Id = $3
            """, conexao, transacao);
        comando.Parameters.AddWithValue((short)chamado.Status);
        comando.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = chamado.TecnicoId.HasValue ? chamado.TecnicoId.Value : DBNull.Value });
        comando.Parameters.AddWithValue(id);
        await comando.ExecuteNonQueryAsync(cancelamento);
        await GravarEventoAsync(conexao, transacao, id, chamado.Historico[^1], cancelamento);
        await transacao.CommitAsync(cancelamento);
        return chamado;
    }

    private static async Task<Chamado?> CarregarAsync(NpgsqlConnection conexao,
        NpgsqlTransaction transacao, Guid id, bool bloquear, CancellationToken cancelamento)
    {
        var sql = """
            SELECT Id, SolicitanteId, TecnicoId, Titulo, Categoria, Descricao, Status
            FROM public.Chamado WHERE Id = $1
            """;
        if (bloquear)
            sql += " FOR UPDATE";

        await using var comando = new NpgsqlCommand(sql, conexao, transacao);
        comando.Parameters.AddWithValue(id);
        Guid solicitanteId;
        Guid? tecnicoId;
        string titulo, categoria, descricao;
        StatusChamado status;
        await using (var leitor = await comando.ExecuteReaderAsync(cancelamento))
        {
            if (!await leitor.ReadAsync(cancelamento))
                return null;
            solicitanteId = leitor.GetGuid(1);
            tecnicoId = leitor.IsDBNull(2) ? null : leitor.GetGuid(2);
            titulo = leitor.GetString(3);
            categoria = leitor.GetString(4);
            descricao = leitor.GetString(5);
            status = (StatusChamado)leitor.GetInt16(6);
        }

        await using var eventos = new NpgsqlCommand("""
            SELECT Tipo, AutorId, Data, Descricao FROM public.EventoChamado
            WHERE ChamadoId = $1 ORDER BY Id
            """, conexao, transacao);
        eventos.Parameters.AddWithValue(id);
        await using var historico = await eventos.ExecuteReaderAsync(cancelamento);
        var registros = new List<EventoChamado>();
        while (await historico.ReadAsync(cancelamento))
            registros.Add(new((TipoEvento)historico.GetInt16(0), historico.GetGuid(1),
                historico.GetFieldValue<DateTimeOffset>(2),
                historico.IsDBNull(3) ? null : historico.GetString(3)));
        return Chamado.Restaurar(id, solicitanteId, tecnicoId, titulo, categoria,
            descricao, status, registros);
    }

    private static async Task GravarEventoAsync(NpgsqlConnection conexao,
        NpgsqlTransaction transacao, Guid id, EventoChamado evento, CancellationToken cancelamento)
    {
        await using var comando = new NpgsqlCommand("""
            INSERT INTO public.EventoChamado (ChamadoId, AutorId, Tipo, Data, Descricao)
            VALUES ($1, $2, $3, $4, $5)
            """, conexao, transacao);
        comando.Parameters.AddWithValue(id);
        comando.Parameters.AddWithValue(evento.AutorId);
        comando.Parameters.AddWithValue((short)evento.Tipo);
        comando.Parameters.AddWithValue(evento.Data);
        comando.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar,
            Value = (object?)evento.Descricao ?? DBNull.Value });
        await comando.ExecuteNonQueryAsync(cancelamento);
    }
}

public sealed record ResumoChamado(Guid Id, string Titulo, string Categoria,
    StatusChamado Status, Guid? TecnicoId);
