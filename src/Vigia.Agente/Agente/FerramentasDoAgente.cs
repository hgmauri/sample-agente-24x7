using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Vigia.Agente.Agente;

/// <summary>
/// O que o agente pode fazer. Leitura à vontade, escrita só registrando, ação destrutiva só com aprovação.
/// </summary>
public sealed class FerramentasDoAgente(
    FonteDeSinais fonte,
    RegistroDeIncidentes incidentes,
    AprovacoesPendentes aprovacoes,
    string assinaturaDoCiclo)
{
    public IList<AITool> Todas() =>
    [
        AIFunctionFactory.Create(VerificarSaudeAsync, "verificar_saude"),
        AIFunctionFactory.Create(ConsultarErrosRecentesAsync, "consultar_erros_recentes"),
        AIFunctionFactory.Create(AbrirIncidenteAsync, "abrir_incidente"),
        AIFunctionFactory.Create(SolicitarReinicio, "solicitar_reinicio"),
    ];

    [Description("Consulta o health check da aplicação e retorna o status de cada dependência.")]
    public async Task<string> VerificarSaudeAsync(CancellationToken ct)
    {
        var (_, status) = await fonte.VerificarSaudeAsync(ct);
        return status;
    }

    [Description("Lista os erros 5xx recentes da aplicação, agrupados por tipo e mensagem.")]
    public async Task<IEnumerable<string>> ConsultarErrosRecentesAsync(
        [Description("Janela em minutos, de 1 a 60.")] int minutos,
        CancellationToken ct)
    {
        var erros = await fonte.ConsultarErrosAsync(Math.Clamp(minutos, 1, 60), ct);

        return erros
            .GroupBy(e => $"{e.Tipo}: {e.Mensagem}")
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => $"{g.Key} ({g.Count()}x em {g.Select(e => e.Rota).Distinct().Count()} rota(s), último às {g.Max(e => e.Quando):HH:mm:ss} UTC)");
    }

    [Description("Abre um incidente para o time de plantão. Use uma única vez por análise.")]
    public async Task<string> AbrirIncidenteAsync(
        [Description("Título curto e objetivo.")] string titulo,
        [Description("Severidade: SEV1 (fora do ar), SEV2 (degradado) ou SEV3 (atenção).")] string severidade,
        [Description("Diagnóstico baseado apenas nos dados consultados.")] string diagnostico,
        [Description("Próximo passo recomendado para o time.")] string acaoRecomendada,
        CancellationToken ct)
    {
        var incidente = await incidentes.AbrirAsync(titulo, severidade, diagnostico, acaoRecomendada, assinaturaDoCiclo, geradoPorIa: true, ct);
        return $"Incidente {incidente.Id} aberto.";
    }

    [Description("Solicita o reinício da aplicação. Não reinicia: cria um pedido que precisa de aprovação humana.")]
    public string SolicitarReinicio([Description("Motivo do reinício, com a evidência.")] string motivo)
    {
        var solicitacao = aprovacoes.Solicitar("reiniciar-aplicacao", motivo);
        return $"Solicitação {solicitacao.Id} registrada. Aguardando aprovação humana.";
    }
}
