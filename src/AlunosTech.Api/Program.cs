using System.Net;
using System.Text.Json.Serialization;
using AlunosTech.Chamados;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
var connectionString = builder.Configuration.GetConnectionString("Chamados");
if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Configure ConnectionStrings:Chamados com a conexão PostgreSQL. Consulte o README.");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString!));
    builder.Services.AddSingleton<ChamadosPostgres>();
}
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async http =>
{
    http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    await http.Response.WriteAsJsonAsync(new { erro =
        "Não foi possível concluir a operação. Confira a conexão e as tabelas do PostgreSQL." });
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// As identidades de teste só existem no ambiente local de desenvolvimento.
if (app.Environment.IsDevelopment())
{
    var chamados = app.Services.GetRequiredService<ChamadosPostgres>();
    var usuarios = new Dictionary<string, UsuarioTeste>(StringComparer.Ordinal)
    {
        ["funcionario"] = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), false),
        ["outro-funcionario"] = new(Guid.Parse("00000000-0000-0000-0000-000000000002"), false),
        ["tecnico"] = new(Guid.Parse("00000000-0000-0000-0000-000000000003"), true),
        ["outro-tecnico"] = new(Guid.Parse("00000000-0000-0000-0000-000000000004"), true)
    };

    var api = app.MapGroup("/api/chamados");
    api.AddEndpointFilter(async (context, next) =>
    {
        var http = context.HttpContext;
        if (http.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var header = http.Request.Headers["X-Usuario-Teste"];
        if (header.Count != 1 || !usuarios.TryGetValue(header.ToString(), out var usuario))
            return Results.Unauthorized();

        http.Items[typeof(UsuarioTeste)] = usuario;
        return await next(context);
    });

    api.MapGet("/", async (HttpContext http, StatusChamado? status, CancellationToken ct) =>
    {
        if (status.HasValue && !Enum.IsDefined(status.Value))
            return Results.BadRequest(new { erro = "Status inválido." });
        var usuario = Usuario(http);
        return Results.Ok(await chamados.ListarAsync(usuario.Id, usuario.Tecnico, status, ct));
    });

    api.MapPost("/", async (HttpContext http, NovoChamado entrada, CancellationToken ct) =>
    {
        var usuario = Usuario(http);
        if (usuario.Tecnico)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            var chamado = new Chamado(usuario.Id, entrada.Titulo, entrada.Categoria,
                entrada.Descricao, DateTimeOffset.UtcNow);
            await chamados.CriarAsync(chamado, ct);
            return Results.Created($"/api/chamados/{chamado.Id}", Detalhes(chamado));
        }
        catch (ArgumentException erro)
        {
            return Results.BadRequest(new { erro = erro.Message });
        }
    });

    api.MapGet("/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) =>
    {
        var chamado = await chamados.BuscarAsync(id, ct);
        return chamado is not null && PodeVer(Usuario(http), chamado)
            ? Results.Ok(Detalhes(chamado))
            : Results.NotFound();
    });

    api.MapPost("/{id:guid}/assumir", (HttpContext http, Guid id, CancellationToken ct) =>
        Atualizar(http, id, (c, tecnico, agora) => c.Assumir(tecnico, agora), ct));
    api.MapPost("/{id:guid}/atualizacoes",
        (HttpContext http, Guid id, TextoAtendimento entrada, CancellationToken ct) =>
        Atualizar(http, id,
            (c, tecnico, agora) => c.RegistrarAtualizacao(tecnico, entrada.Texto, agora), ct));
    api.MapPost("/{id:guid}/resolver",
        (HttpContext http, Guid id, TextoAtendimento entrada, CancellationToken ct) =>
        Atualizar(http, id, (c, tecnico, agora) => c.Resolver(tecnico, entrada.Texto, agora), ct));

    async Task<IResult> Atualizar(HttpContext http, Guid id,
        Action<Chamado, Guid, DateTimeOffset> acao, CancellationToken ct)
    {
        var usuario = Usuario(http);
        if (!usuario.Tecnico)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            var chamado = await chamados.AtualizarAsync(id,
                c => acao(c, usuario.Id, DateTimeOffset.UtcNow), ct);
            return chamado is null ? Results.NotFound() : Results.Ok(Detalhes(chamado));
        }
        catch (InvalidOperationException erro)
        {
            return Results.Conflict(new { erro = erro.Message });
        }
        catch (ArgumentException erro)
        {
            return Results.BadRequest(new { erro = erro.Message });
        }
    }
}

app.Run();

static UsuarioTeste Usuario(HttpContext http) => (UsuarioTeste)http.Items[typeof(UsuarioTeste)]!;
static bool PodeVer(UsuarioTeste usuario, Chamado chamado) =>
    usuario.Tecnico || chamado.SolicitanteId == usuario.Id;

static object Detalhes(Chamado c) => new
{
    c.Id, c.Titulo, c.Categoria, c.Descricao, c.Status, c.SolicitanteId,
    c.TecnicoId, Historico = c.Historico.ToArray()
};

record UsuarioTeste(Guid Id, bool Tecnico);
record NovoChamado(string Titulo, string Categoria, string Descricao);
record TextoAtendimento(string Texto);
