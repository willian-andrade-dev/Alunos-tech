namespace AlunosTech.Chamados;

public enum StatusChamado { Aberto, EmAtendimento, Resolvido }
public enum TipoEvento { Abertura, Atendimento, Atualizacao, Resolucao }

public sealed record EventoChamado(
    TipoEvento Tipo, Guid AutorId, DateTimeOffset Data, string? Descricao);

public sealed class Chamado
{
    private readonly List<EventoChamado> _historico = [];

    public Guid Id { get; } = Guid.NewGuid();
    public Guid SolicitanteId { get; }
    public Guid? TecnicoId { get; private set; }
    public string Titulo { get; }
    public string Categoria { get; }
    public string Descricao { get; }
    public StatusChamado Status { get; private set; } = StatusChamado.Aberto;
    public IReadOnlyList<EventoChamado> Historico => _historico.AsReadOnly();

    public Chamado(Guid solicitanteId, string titulo, string categoria, string descricao,
        DateTimeOffset agora)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(solicitanteId, Guid.Empty);
        SolicitanteId = solicitanteId;
        Titulo = ValidarTexto(titulo, 120);
        Categoria = ValidarTexto(categoria, 60);
        Descricao = ValidarTexto(descricao, 2000);
        _historico.Add(new(TipoEvento.Abertura, solicitanteId, agora, null));
    }

    public void Assumir(Guid tecnicoId, DateTimeOffset agora)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tecnicoId, Guid.Empty);
        if (Status != StatusChamado.Aberto)
            throw new InvalidOperationException("Apenas chamados abertos podem ser assumidos.");

        TecnicoId = tecnicoId;
        Status = StatusChamado.EmAtendimento;
        _historico.Add(new(TipoEvento.Atendimento, tecnicoId, agora, null));
    }

    public void RegistrarAtualizacao(Guid tecnicoId, string descricao, DateTimeOffset agora)
    {
        ValidarAtendimento(tecnicoId);
        _historico.Add(new(TipoEvento.Atualizacao, tecnicoId, agora, ValidarTexto(descricao, 2000)));
    }

    public void Resolver(Guid tecnicoId, string solucao, DateTimeOffset agora)
    {
        ValidarAtendimento(tecnicoId);
        var texto = ValidarTexto(solucao, 2000);
        Status = StatusChamado.Resolvido;
        _historico.Add(new(TipoEvento.Resolucao, tecnicoId, agora, texto));
    }

    private void ValidarAtendimento(Guid tecnicoId)
    {
        if (Status != StatusChamado.EmAtendimento || TecnicoId != tecnicoId)
            throw new InvalidOperationException("O chamado precisa estar em atendimento pelo técnico responsável.");
    }

    private static string ValidarTexto(string texto, int limite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        var valor = texto.Trim();
        if (valor.Length > limite)
            throw new ArgumentException($"O texto deve ter até {limite} caracteres.");

        return valor;
    }
}
