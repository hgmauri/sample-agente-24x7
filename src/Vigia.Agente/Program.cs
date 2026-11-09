using Vigia.Agente.Agente;
using Vigia.Agente.Infra;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAgenteDePlantao(builder.Configuration);

// Um erro em um serviço em segundo plano não deve derrubar o processo inteiro
builder.Services.Configure<HostOptions>(o =>
{
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    o.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// Liveness probe do Container Apps: falha se o agente parar de bater
app.MapHealthChecks("/health");

app.MapGet("/status", (EstadoDoAgente estado, OrcamentoDeTokens orcamento) => new
{
    estado.UltimoCiclo,
    estado.Ciclos,
    estado.CiclosComFalha,
    estado.ChamadasDeIa,
    estado.UltimoResultado,
    TokensHoje = orcamento.ConsumidoHoje
});

app.MapGet("/incidentes", (RegistroDeIncidentes incidentes) => incidentes.Listar());
app.MapGet("/aprovacoes", (AprovacoesPendentes aprovacoes) => aprovacoes.Listar());

// Em produção, proteja este endpoint (Entra ID) ou mova a aprovação para o Teams
app.MapPost("/aprovacoes/{id}/aprovar", (string id, AprovacoesPendentes aprovacoes) =>
    aprovacoes.Aprovar(id) is { } aprovada ? Results.Ok(aprovada) : Results.NotFound());

app.Run();
