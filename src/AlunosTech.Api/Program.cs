using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.RateLimiting;
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
    builder.Services.AddSingleton<Autenticacao>();
}
builder.Services.AddAuthentication(BearerTokenDefaults.AuthenticationScheme)
    .AddBearerToken(options => options.BearerTokenExpiration = TimeSpan.FromHours(1));
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
var app = builder.Build();
if (builder.Configuration.GetValue<bool>("criar-usuarios-teste"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("A configuração de contas de teste exige Development.");

    var senhas = app.Services.GetRequiredService<Autenticacao>();
    await senhas.ConfigurarContasTesteAsync(
        SenhaConsole.Ler("Defina a senha dos funcionários de teste (mínimo 12 caracteres): "),
        SenhaConsole.Ler("Defina a senha dos técnicos de teste (mínimo 12 caracteres): "),
        CancellationToken.None);
    Console.WriteLine("Senhas configuradas para as quatro contas de teste.");
    await app.DisposeAsync();
    return;
}
app.UseExceptionHandler(handler => handler.Run(async http =>
{
    http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    await http.Response.WriteAsJsonAsync(new { erro =
        "Não foi possível concluir a operação. Confira a conexão e as tabelas do PostgreSQL." });
}));

if (app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers.CacheControl = "no-store";
            context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Context.Response.Headers["Referrer-Policy"] = "same-origin";
            context.Context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; " +
                "img-src 'self'; connect-src 'self'; object-src 'none'; " +
                "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        }
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Até a etapa de publicação, as rotas ficam restritas ao desenvolvimento local.
if (app.Environment.IsDevelopment())
{
    var chamados = app.Services.GetRequiredService<ChamadosPostgres>();
    var autenticacao = app.Services.GetRequiredService<Autenticacao>();
    var local = app.MapGroup("/api");
    local.AddEndpointFilter(async (context, next) =>
    {
        var ip = context.HttpContext.Connection.RemoteIpAddress;
        return ip is not null && IPAddress.IsLoopback(ip)
            ? await next(context)
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    });

    local.MapPost("/auth/login", async (EntradaLogin entrada, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(entrada.Email) || entrada.Email.Length > 254 ||
            string.IsNullOrEmpty(entrada.Senha) || entrada.Senha.Length > 128)
            return Results.BadRequest(new { erro = "Informe e-mail e senha válidos." });

        var usuario = await autenticacao.EntrarAsync(entrada.Email, entrada.Senha, ct);
        return usuario is null
            ? Results.Json(new { erro = "E-mail ou senha inválidos." }, statusCode: 401)
            : Results.SignIn(Autenticacao.CriarIdentidade(usuario),
                authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
    }).RequireRateLimiting("login");

    var protegidas = local.MapGroup("").RequireAuthorization();
    protegidas.AddEndpointFilter(async (context, next) =>
    {
        var http = context.HttpContext;
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Results.Unauthorized();

        var usuario = await autenticacao.BuscarAsync(id, http.RequestAborted);
        if (usuario is null || !usuario.Ativo ||
            http.User.FindFirstValue("versao_credencial") != usuario.VersaoCredencial.ToString())
            return Results.Unauthorized();

        http.Items[typeof(UsuarioConta)] = usuario;
        return await next(context);
    });
    protegidas.MapGet("/auth/me", (HttpContext http) =>
    {
        var usuario = Usuario(http);
        return Results.Ok(new { usuario.Id, usuario.Nome, usuario.Email,
            Perfil = usuario.Tecnico ? "Tecnico" : "Funcionario" });
    });

    var api = protegidas.MapGroup("/chamados");

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

static UsuarioConta Usuario(HttpContext http) => (UsuarioConta)http.Items[typeof(UsuarioConta)]!;
static bool PodeVer(UsuarioConta usuario, Chamado chamado) =>
    usuario.Tecnico || chamado.SolicitanteId == usuario.Id;

static object Detalhes(Chamado c) => new
{
    c.Id, c.Titulo, c.Categoria, c.Descricao, c.Status, c.SolicitanteId,
    c.TecnicoId, Historico = c.Historico.ToArray()
};

record EntradaLogin(string Email, string Senha);
record NovoChamado(string Titulo, string Categoria, string Descricao);
record TextoAtendimento(string Texto);
