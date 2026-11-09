using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Vigia.Agente.Agente;

public sealed record Incidente(
    string Id,
    DateTimeOffset AbertoEm,
    string Titulo,
    string Severidade,
    string Diagnostico,
    string AcaoRecomendada,
    string Assinatura,
    bool GeradoPorIa);

public sealed class NotificacaoOptions
{
    /// <summary>Webhook genérico (Teams Workflows, Slack, Power Automate, seu próprio endpoint).</summary>
    public string? WebhookUrl { get; set; }
}

/// <summary>
/// Guarda os incidentes e avisa o time. Em produção: Azure DevOps, Jira, ServiceNow ou PagerDuty.
/// </summary>
public sealed class RegistroDeIncidentes(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificacaoOptions> notificacao,
    TimeProvider relogio,
    ILogger<RegistroDeIncidentes> logger)
{
    private readonly ConcurrentQueue<Incidente> _incidentes = new();

    public IReadOnlyList<Incidente> Listar() => [.. _incidentes.Reverse()];

    public bool HouveIncidenteRecente(string assinatura, TimeSpan intervalo) =>
        _incidentes.Any(i => i.Assinatura == assinatura && relogio.GetUtcNow() - i.AbertoEm < intervalo);

    public async Task<Incidente> AbrirAsync(
        string titulo, string severidade, string diagnostico, string acaoRecomendada,
        string assinatura, bool geradoPorIa, CancellationToken ct = default)
    {
        var incidente = new Incidente(
            $"INC-{relogio.GetUtcNow():yyyyMMddHHmmss}",
            relogio.GetUtcNow(), titulo, severidade, diagnostico, acaoRecomendada, assinatura, geradoPorIa);

        _incidentes.Enqueue(incidente);
        logger.LogWarning("Incidente {Id} [{Severidade}] {Titulo}", incidente.Id, severidade, titulo);

        if (!string.IsNullOrWhiteSpace(notificacao.Value.WebhookUrl))
        {
            try
            {
                await httpClientFactory.CreateClient().PostAsJsonAsync(notificacao.Value.WebhookUrl, incidente, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Falha ao notificar não pode derrubar o agente
                logger.LogError(ex, "Falha ao notificar o incidente {Id}", incidente.Id);
            }
        }

        return incidente;
    }
}

public sealed record SolicitacaoDeAcao(string Id, DateTimeOffset CriadaEm, string Acao, string Motivo, string Status);

/// <summary>
/// Ações com efeito colateral nunca são executadas pela IA. Elas viram uma solicitação
/// que uma pessoa aprova. É o human-in-the-loop que deixa o agente rodar 24/7 com segurança.
/// </summary>
public sealed class AprovacoesPendentes(TimeProvider relogio, ILogger<AprovacoesPendentes> logger)
{
    private readonly ConcurrentDictionary<string, SolicitacaoDeAcao> _solicitacoes = new();

    public IReadOnlyList<SolicitacaoDeAcao> Listar() => [.. _solicitacoes.Values.OrderByDescending(s => s.CriadaEm)];

    public SolicitacaoDeAcao Solicitar(string acao, string motivo)
    {
        var solicitacao = new SolicitacaoDeAcao(
            $"ACAO-{relogio.GetUtcNow():yyyyMMddHHmmss}", relogio.GetUtcNow(), acao, motivo, "AguardandoAprovacao");

        _solicitacoes[solicitacao.Id] = solicitacao;
        logger.LogWarning("Ação {Acao} aguardando aprovação humana: {Motivo}", acao, motivo);
        return solicitacao;
    }

    public SolicitacaoDeAcao? Aprovar(string id)
    {
        if (!_solicitacoes.TryGetValue(id, out var atual) || atual.Status != "AguardandoAprovacao")
            return null;

        // Aqui entraria a chamada real: Azure Container Apps (restart de revisão), App Service, Kubernetes...
        var aprovada = atual with { Status = "Aprovada" };
        _solicitacoes[id] = aprovada;
        logger.LogWarning("Ação {Id} aprovada", id);
        return aprovada;
    }
}
