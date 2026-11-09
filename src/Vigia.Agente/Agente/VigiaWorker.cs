using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Vigia.Agente.Infra;

namespace Vigia.Agente.Agente;

/// <summary>
/// O agente 24/7: um BackgroundService que acorda a cada ciclo, olha a aplicação
/// e só chama a IA quando existe algo para investigar.
/// </summary>
public sealed class VigiaWorker(
    IChatClient chat,
    FonteDeSinais fonte,
    RegistroDeIncidentes incidentes,
    AprovacoesPendentes aprovacoes,
    OrcamentoDeTokens orcamento,
    EstadoDoAgente estado,
    IOptions<VigiaOptions> vigia,
    IOptions<FoundryOptions> foundry,
    ILogger<VigiaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Agente iniciado. Ciclo a cada {Intervalo}", vigia.Value.IntervaloDoCiclo);

        // PeriodicTimer não acumula ticks: se um ciclo atrasar, o próximo não roda em paralelo
        using var timer = new PeriodicTimer(vigia.Value.IntervaloDoCiclo);

        do
        {
            await ExecutarCicloAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecutarCicloAsync(CancellationToken stoppingToken)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        limite.CancelAfter(vigia.Value.TempoMaximoPorCiclo);

        try
        {
            var resultado = await AnalisarAsync(limite.Token);
            estado.Registrar(resultado.Descricao, usouIa: resultado.UsouIa);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Ciclo passou de {Limite} e foi cancelado", vigia.Value.TempoMaximoPorCiclo);
            estado.Registrar("Ciclo cancelado por tempo", falhou: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Um ciclo com erro nunca derruba o agente. Registra e segue para o próximo.
            logger.LogError(ex, "Falha no ciclo do agente");
            estado.Registrar($"Falha: {ex.GetType().Name}", falhou: true);
        }
    }

    private async Task<(string Descricao, bool UsouIa)> AnalisarAsync(CancellationToken ct)
    {
        var opcoes = vigia.Value;
        var sinais = await fonte.ColetarAsync(opcoes.JanelaDeErrosEmMinutos, ct);

        // 1. Regra primeiro: com tudo saudável, nenhum token é gasto
        if (!sinais.PrecisaDeAtencao(opcoes.LimiteDeErros))
            return ("Aplicação saudável", false);

        // 2. Mesmo problema de 10 minutos atrás? Não abre outro incidente nem chama a IA de novo
        var assinatura = sinais.Assinatura();
        if (incidentes.HouveIncidenteRecente(assinatura, opcoes.IntervaloEntreIncidentes))
            return ("Problema já reportado, aguardando o time", false);

        // 3. Orçamento do dia esgotado: incidente só com os dados das regras
        if (!orcamento.TemSaldo)
        {
            await AbrirSemIaAsync(sinais, assinatura, "Orçamento diário de tokens esgotado", ct);
            return ("Incidente aberto sem IA (orçamento)", false);
        }

        // 4. Agora sim, a IA investiga usando as ferramentas
        var ferramentas = new FerramentasDoAgente(fonte, incidentes, aprovacoes, assinatura);
        List<ChatMessage> mensagens =
        [
            new(ChatRole.System, PromptDoAgente.Sistema),
            new(ChatRole.User, $"Sinais coletados agora ({DateTimeOffset.UtcNow:O}):\n{JsonSerializer.Serialize(sinais)}")
        ];

        try
        {
            var resposta = await chat.GetResponseAsync(mensagens, new ChatOptions
            {
                Tools = ferramentas.Todas(),
                MaxOutputTokens = foundry.Value.MaxTokensResposta
            }, ct);

            orcamento.Consumir(resposta.Usage);
            logger.LogInformation("Análise da IA: {Resumo} ({Tokens} tokens)", resposta.Text, resposta.Usage?.TotalTokenCount);

            // Garantia: se o modelo não abriu incidente, o agente abre
            if (!incidentes.HouveIncidenteRecente(assinatura, opcoes.IntervaloEntreIncidentes))
                await AbrirSemIaAsync(sinais, assinatura, "O modelo não abriu incidente", ct);

            return ("Incidente analisado pela IA", true);
        }
        catch (CotaEsgotadaException ex)
        {
            // Cota do Foundry estourou em todos os deployments: o plantão continua sem IA
            logger.LogWarning("Cota do Foundry esgotada. Nova tentativa após {Espera}", ex.TentarApos);
            await AbrirSemIaAsync(sinais, assinatura, $"Cota do Foundry esgotada (429). IA indisponível por {ex.TentarApos}", ct);
            return ("Incidente aberto sem IA (cota)", false);
        }
    }

    private Task AbrirSemIaAsync(SinaisDaAplicacao sinais, string assinatura, string motivo, CancellationToken ct) =>
        incidentes.AbrirAsync(
            titulo: sinais.Saudavel ? $"{sinais.TotalDeErros} erros nos últimos {vigia.Value.JanelaDeErrosEmMinutos} min" : "Health check falhando",
            severidade: sinais.Saudavel ? "SEV3" : "SEV2",
            diagnostico: $"{motivo}. Sinais: {string.Join("; ", sinais.PrincipaisErros)}",
            acaoRecomendada: "Investigar manualmente",
            assinatura: assinatura,
            geradoPorIa: false,
            ct: ct);
}
