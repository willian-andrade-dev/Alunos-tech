using System.Net;
using System.Text.Json.Serialization;
using AlunosTech.Chamados;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// As identidades de teste só existem no ambiente local de desenvolvimento.
if (app.Environment.IsDevelopment())
{
    var chamados = new Dictionary<Guid, Chamado>();
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

    api.MapGet("/", (HttpContext http, StatusChamado? status) =>
    {
        var usuario = Usuario(http);
        lock (chamados)
            return Results.Ok(chamados.Values
                .Where(c => PodeVer(usuario, c) && (status is null || c.Status == status))
                .OrderByDescending(c => c.Historico[0].Data)
                .Select(Resumo)
                .ToArray());
    });

    api.MapPost("/", (HttpContext http, NovoChamado entrada) =>
    {
        var usuario = Usuario(http);
        if (usuario.Tecnico)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            var chamado = new Chamado(usuario.Id, entrada.Titulo, entrada.Categoria,
                entrada.Descricao, DateTimeOffset.UtcNow);
            lock (chamados)
            {
                chamados.Add(chamado.Id, chamado);
                return Results.Created($"/api/chamados/{chamado.Id}", Detalhes(chamado));
            }
        }
        catch (ArgumentException erro)
        {
            return Results.BadRequest(new { erro = erro.Message });
        }
    });

    api.MapGet("/{id:guid}", (HttpContext http, Guid id) =>
    {
        lock (chamados)
            return chamados.TryGetValue(id, out var chamado) && PodeVer(Usuario(http), chamado)
                ? Results.Ok(Detalhes(chamado))
                : Results.NotFound();
    });

    api.MapPost("/{id:guid}/assumir", (HttpContext http, Guid id) =>
        Atualizar(http, id, (c, tecnico, agora) => c.Assumir(tecnico, agora)));
    api.MapPost("/{id:guid}/atualizacoes", (HttpContext http, Guid id, TextoAtendimento entrada) =>
        Atualizar(http, id, (c, tecnico, agora) => c.RegistrarAtualizacao(tecnico, entrada.Texto, agora)));
    api.MapPost("/{id:guid}/resolver", (HttpContext http, Guid id, TextoAtendimento entrada) =>
        Atualizar(http, id, (c, tecnico, agora) => c.Resolver(tecnico, entrada.Texto, agora)));

    IResult Atualizar(HttpContext http, Guid id, Action<Chamado, Guid, DateTimeOffset> acao)
    {
        var usuario = Usuario(http);
        if (!usuario.Tecnico)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        lock (chamados)
        {
            if (!chamados.TryGetValue(id, out var chamado))
                return Results.NotFound();

            try
            {
                acao(chamado, usuario.Id, DateTimeOffset.UtcNow);
                return Results.Ok(Detalhes(chamado));
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
}

app.Run();

static UsuarioTeste Usuario(HttpContext http) => (UsuarioTeste)http.Items[typeof(UsuarioTeste)]!;
static bool PodeVer(UsuarioTeste usuario, Chamado chamado) =>
    usuario.Tecnico || chamado.SolicitanteId == usuario.Id;

static object Resumo(Chamado c) => new { c.Id, c.Titulo, c.Categoria, c.Status, c.TecnicoId };
static object Detalhes(Chamado c) => new
{
    c.Id, c.Titulo, c.Categoria, c.Descricao, c.Status, c.SolicitanteId,
    c.TecnicoId, Historico = c.Historico.ToArray()
};

record UsuarioTeste(Guid Id, bool Tecnico);
record NovoChamado(string Titulo, string Categoria, string Descricao);
record TextoAtendimento(string Texto);
