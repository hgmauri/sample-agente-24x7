using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Vigia.Agente.Agente;

/// <summary>
/// Agente 24/7 sem teto de gasto é conta surpresa garantida. Zera a cada dia (UTC).
/// </summary>
public sealed class OrcamentoDeTokens(IOptions<VigiaOptions> opcoes, TimeProvider relogio)
{
    private readonly Lock _trava = new();
    private DateOnly _dia;
    private long _consumido;

    public long ConsumidoHoje { get { lock (_trava) { Virar(); return _consumido; } } }

    public bool TemSaldo => ConsumidoHoje < opcoes.Value.OrcamentoDiarioDeTokens;

    public void Consumir(UsageDetails? uso)
    {
        if (uso?.TotalTokenCount is not { } tokens) return;
        lock (_trava) { Virar(); _consumido += tokens; }
    }

    private void Virar()
    {
        var hoje = DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime);
        if (hoje == _dia) return;
        _dia = hoje;
        _consumido = 0;
    }
}
