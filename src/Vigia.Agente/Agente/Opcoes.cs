namespace Vigia.Agente.Agente;

public sealed class VigiaOptions
{
    public string UrlDaAplicacao { get; set; } = "http://localhost:5100";

    /// <summary>De quanto em quanto tempo o agente olha a aplicação.</summary>
    public TimeSpan IntervaloDoCiclo { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Teto de duração de um ciclo. Nada pode travar o agente para sempre.</summary>
    public TimeSpan TempoMaximoPorCiclo { get; set; } = TimeSpan.FromMinutes(2);

    public int JanelaDeErrosEmMinutos { get; set; } = 15;

    /// <summary>A partir de quantos erros na janela o agente chama a IA.</summary>
    public int LimiteDeErros { get; set; } = 5;

    /// <summary>Evita abrir o mesmo incidente a cada minuto.</summary>
    public TimeSpan IntervaloEntreIncidentes { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Teto de tokens por dia (UTC). Acabou o saldo, o agente segue só com regras.</summary>
    public long OrcamentoDiarioDeTokens { get; set; } = 200_000;
}

public sealed class FoundryOptions
{
    public string? Endpoint { get; set; }
    public string Deployment { get; set; } = "gpt-5.4-mini";

    /// <summary>Deployment em outra região ou assinatura, usado quando a cota do principal estoura.</summary>
    public string? EndpointReserva { get; set; }
    public string DeploymentReserva { get; set; } = "gpt-5.4-mini";

    /// <summary>Quanto menor, menos cota cada chamada reserva (o Foundry estima a cota pelo max_tokens).</summary>
    public int MaxTokensResposta { get; set; } = 2000;
}
