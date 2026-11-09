using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Vigia.Agente.Agente;

/// <summary>Batimento cardíaco do agente. É o que a plataforma usa para saber se ele travou.</summary>
public sealed class EstadoDoAgente(TimeProvider relogio)
{
    public DateTimeOffset? UltimoCiclo { get; private set; }
    public long Ciclos { get; private set; }
    public long CiclosComFalha { get; private set; }
    public long ChamadasDeIa { get; private set; }
    public string UltimoResultado { get; private set; } = "Iniciando";

    public void Registrar(string resultado, bool falhou = false, bool usouIa = false)
    {
        UltimoCiclo = relogio.GetUtcNow();
        Ciclos++;
        if (falhou) CiclosComFalha++;
        if (usouIa) ChamadasDeIa++;
        UltimoResultado = resultado;
    }
}

public sealed class AgenteVivoHealthCheck(EstadoDoAgente estado, IOptions<VigiaOptions> opcoes, TimeProvider relogio) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Três ciclos sem batimento: o agente travou e a plataforma deve reiniciar o container
        var limite = opcoes.Value.IntervaloDoCiclo * 3 + opcoes.Value.TempoMaximoPorCiclo;

        if (estado.UltimoCiclo is { } ultimo && relogio.GetUtcNow() - ultimo > limite)
            return Task.FromResult(HealthCheckResult.Unhealthy($"Sem ciclo desde {ultimo:O}"));

        return Task.FromResult(HealthCheckResult.Healthy(estado.UltimoResultado));
    }
}
