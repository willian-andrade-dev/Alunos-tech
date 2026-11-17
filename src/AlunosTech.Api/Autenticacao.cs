using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Npgsql;

sealed record UsuarioConta(Guid Id, string Nome, string Email, bool Tecnico,
    string? SenhaHash, bool Ativo, Guid VersaoCredencial);

sealed class Autenticacao(NpgsqlDataSource banco)
{
    private readonly PasswordHasher<object> _senhas = new(Options.Create(
        new PasswordHasherOptions { IterationCount = 210_000 }));
    private readonly object _contextoSenha = new();
    private readonly string _hashSimulado = new PasswordHasher<object>(Options.Create(
        new PasswordHasherOptions { IterationCount = 210_000 }))
        .HashPassword(new object(), Guid.NewGuid().ToString());

    public async Task<UsuarioConta?> EntrarAsync(string email, string senha, CancellationToken ct)
    {
        await using var comando = banco.CreateCommand("""
            SELECT Id, Nome, Email, Perfil, SenhaHash, Ativo, VersaoCredencial
            FROM public.Usuario WHERE lower(Email) = lower($1)
            """);
        comando.Parameters.AddWithValue(email.Trim());
        UsuarioConta? usuario;
        await using (var leitor = await comando.ExecuteReaderAsync(ct))
            usuario = await leitor.ReadAsync(ct) ? LerUsuario(leitor) : null;

        // Mantém o custo de verificação mesmo para contas sem senha ou inexistentes.
        var resultado = _senhas.VerifyHashedPassword(_contextoSenha,
            usuario?.SenhaHash ?? _hashSimulado, senha);
        if (usuario is null || !usuario.Ativo || usuario.SenhaHash is null ||
            resultado == PasswordVerificationResult.Failed)
            return null;

        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await using var atualizar = banco.CreateCommand("""
                UPDATE public.Usuario SET SenhaHash = $1
                WHERE Id = $2 AND SenhaHash = $3 AND VersaoCredencial = $4
                """);
            atualizar.Parameters.AddWithValue(_senhas.HashPassword(_contextoSenha, senha));
            atualizar.Parameters.AddWithValue(usuario.Id);
            atualizar.Parameters.AddWithValue(usuario.SenhaHash);
            atualizar.Parameters.AddWithValue(usuario.VersaoCredencial);
            await atualizar.ExecuteNonQueryAsync(ct);
        }
        return usuario;
    }

    public async Task<UsuarioConta?> BuscarAsync(Guid id, CancellationToken ct)
    {
        await using var comando = banco.CreateCommand("""
            SELECT Id, Nome, Email, Perfil, SenhaHash, Ativo, VersaoCredencial
            FROM public.Usuario WHERE Id = $1
            """);
        comando.Parameters.AddWithValue(id);
        await using var leitor = await comando.ExecuteReaderAsync(ct);
        return await leitor.ReadAsync(ct) ? LerUsuario(leitor) : null;
    }

    public static ClaimsPrincipal CriarIdentidade(UsuarioConta usuario)
    {
        var identidade = new ClaimsIdentity(BearerTokenDefaults.AuthenticationScheme);
        identidade.AddClaim(new(ClaimTypes.NameIdentifier, usuario.Id.ToString()));
        identidade.AddClaim(new("versao_credencial", usuario.VersaoCredencial.ToString()));
        return new ClaimsPrincipal(identidade);
    }

    public async Task ConfigurarContasTesteAsync(string senhaFuncionario, string senhaTecnico,
        CancellationToken ct)
    {
        ValidarSenha(senhaFuncionario);
        ValidarSenha(senhaTecnico);
        var contas = new[]
        {
            (Id: "00000000-0000-0000-0000-000000000001", Email: "funcionario@example.invalid", Senha: senhaFuncionario),
            (Id: "00000000-0000-0000-0000-000000000002", Email: "outro.funcionario@example.invalid", Senha: senhaFuncionario),
            (Id: "00000000-0000-0000-0000-000000000003", Email: "tecnico@example.invalid", Senha: senhaTecnico),
            (Id: "00000000-0000-0000-0000-000000000004", Email: "outro.tecnico@example.invalid", Senha: senhaTecnico)
        };

        await using var conexao = await banco.OpenConnectionAsync(ct);
        await using var transacao = await conexao.BeginTransactionAsync(ct);
        foreach (var conta in contas)
        {
            await using var comando = new NpgsqlCommand("""
                UPDATE public.Usuario
                SET SenhaHash = $1, VersaoCredencial = gen_random_uuid()
                WHERE Id = $2 AND Email = $3
                """, conexao, transacao);
            comando.Parameters.AddWithValue(_senhas.HashPassword(_contextoSenha, conta.Senha));
            comando.Parameters.AddWithValue(Guid.Parse(conta.Id));
            comando.Parameters.AddWithValue(conta.Email);
            if (await comando.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidOperationException(
                    "Uma conta de teste está ausente ou foi alterada. Confira o script 002.");
        }
        await transacao.CommitAsync(ct);
    }

    private static void ValidarSenha(string senha)
    {
        if (senha.Length < 12 || senha.Length > 128 || string.IsNullOrWhiteSpace(senha))
            throw new ArgumentException("Use senhas de 12 a 128 caracteres.");
    }

    private static UsuarioConta LerUsuario(NpgsqlDataReader leitor) => new(
        leitor.GetGuid(0), leitor.GetString(1), leitor.GetString(2), leitor.GetInt16(3) == 1,
        leitor.IsDBNull(4) ? null : leitor.GetString(4), leitor.GetBoolean(5), leitor.GetGuid(6));
}

static class SenhaConsole
{
    public static string Ler(string mensagem)
    {
        Console.Write(mensagem);
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? "";

        var caracteres = new List<char>();
        ConsoleKeyInfo tecla;
        while ((tecla = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (caracteres.Count > 0)
                    caracteres.RemoveAt(caracteres.Count - 1);
            }
            else if (!char.IsControl(tecla.KeyChar))
            {
                caracteres.Add(tecla.KeyChar);
            }
        }
        Console.WriteLine();
        return new string(caracteres.ToArray());
    }
}
