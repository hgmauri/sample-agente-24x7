using Microsoft.Extensions.AI;

namespace Vigia.Agente.Infra;

/// <summary>
/// Usado quando o Foundry não está configurado. Simula o raciocínio de um modelo chamando
/// as ferramentas de verdade, para você rodar o agente inteiro sem Azure e sem custo.
/// </summary>
public sealed class ChatClientSimulado : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var lista = messages.ToList();
        var resultados = lista.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        var uso = new UsageDetails { InputTokenCount = 900, OutputTokenCount = 120, TotalTokenCount = 1020 };

        // Passo 1: investigar
        if (resultados.Count == 0)
            return Responder(uso,
                new FunctionCallContent("c1", "verificar_saude"),
                new FunctionCallContent("c2", "consultar_erros_recentes", new Dictionary<string, object?> { ["minutos"] = 15 }));

        // Passo 2: concluir e abrir o incidente com base no que as ferramentas devolveram
        if (resultados.Count == 2 && !lista.Any(m => m.Contents.OfType<FunctionCallContent>().Any(c => c.Name == "abrir_incidente")))
        {
            var evidencias = string.Join(" | ", resultados.Select(r => r.Result?.ToString()));
            var semResposta = evidencias.Contains("Sem resposta", StringComparison.OrdinalIgnoreCase);
            var bancoFora = evidencias.Contains("banco", StringComparison.OrdinalIgnoreCase)
                         || evidencias.Contains("Timeout", StringComparison.OrdinalIgnoreCase);

            if (semResposta)
                return Responder(uso,
                    new FunctionCallContent("c3", "abrir_incidente", new Dictionary<string, object?>
                    {
                        ["titulo"] = "Loja.Api sem resposta",
                        ["severidade"] = "SEV1",
                        ["diagnostico"] = $"[simulado] A aplicação não respondeu ao health check. Evidências: {evidencias}",
                        ["acaoRecomendada"] = "Verificar se o container está em execução e os logs de inicialização"
                    }),
                    new FunctionCallContent("c4", "solicitar_reinicio", new Dictionary<string, object?>
                    {
                        ["motivo"] = "Health check sem resposta (connection refused)"
                    }));

            return Responder(uso,
                new FunctionCallContent("c3", "abrir_incidente", new Dictionary<string, object?>
                {
                    ["titulo"] = bancoFora ? "Pedidos indisponível: timeout no banco de dados" : "Aumento de erros 5xx",
                    ["severidade"] = bancoFora ? "SEV2" : "SEV3",
                    ["diagnostico"] = $"[simulado] Evidências: {evidencias}",
                    ["acaoRecomendada"] = bancoFora ? "Verificar conectividade e carga do banco antes de reiniciar a API" : "Analisar os logs da rota com mais erros"
                }));
        }

        // Passo 3: resumo final
        return Responder(uso, new TextContent("[simulado] Incidente aberto com diagnóstico baseado no health check e nos erros recentes."));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private static Task<ChatResponse> Responder(UsageDetails uso, params AIContent[] conteudo) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, conteudo)) { Usage = uso });
}
