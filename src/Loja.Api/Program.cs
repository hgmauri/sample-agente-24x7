using System.Collections.Concurrent;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// Aplicação de exemplo que o agente vai vigiar.
// Tem health check, um endpoint de negócio e uma forma de simular falhas.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Simulador>();
builder.Services.AddSingleton<UltimosErros>();
builder.Services.AddHealthChecks()
    .AddCheck("banco-de-dados", () => Simulador.BancoFora
        ? HealthCheckResult.Unhealthy("Timeout ao conectar no banco (30s)")
        : HealthCheckResult.Healthy());

var app = builder.Build();

// Guarda os últimos erros 5xx, como um Application Insights bem simplificado
app.Use(async (contexto, proximo) =>
{
    try
    {
        await proximo(contexto);
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<UltimosErros>().Registrar(contexto.Request.Path, ex);
        contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;
    }
});

// Health check com o detalhe de cada dependência, que é o que o agente precisa para diagnosticar
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = (contexto, relatorio) => contexto.Response.WriteAsJsonAsync(new
    {
        status = relatorio.Status.ToString(),
        dependencias = relatorio.Entries.Select(e => new { nome = e.Key, status = e.Value.Status.ToString(), e.Value.Description })
    })
});

app.MapGet("/pedidos/{id:int}", (int id) => Simulador.BancoFora
    ? throw new TimeoutException("Timeout ao executar consulta em Pedidos (30s)")
    : Results.Ok(new { id, status = "EmTransito" }));

app.MapGet("/diagnostico/erros", (int? minutos, UltimosErros erros) =>
    erros.Listar(TimeSpan.FromMinutes(minutos ?? 15)));

app.MapPost("/simular/falha", () => { Simulador.BancoFora = true; return Results.Ok("Banco fora do ar"); });
app.MapPost("/simular/normal", () => { Simulador.BancoFora = false; return Results.Ok("Tudo normal"); });

app.Run();

sealed class Simulador
{
    public static volatile bool BancoFora;
}

sealed record ErroRegistrado(DateTimeOffset Quando, string Rota, string Tipo, string Mensagem);

sealed class UltimosErros
{
    private readonly ConcurrentQueue<ErroRegistrado> _fila = new();

    public void Registrar(string rota, Exception ex)
    {
        _fila.Enqueue(new(DateTimeOffset.UtcNow, rota, ex.GetType().Name, ex.Message));
        while (_fila.Count > 200 && _fila.TryDequeue(out _)) { }
    }

    public IReadOnlyList<ErroRegistrado> Listar(TimeSpan janela) =>
        [.. _fila.Where(e => e.Quando >= DateTimeOffset.UtcNow - janela)];
}
