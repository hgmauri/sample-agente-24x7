using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Vigia.Agente.Agente;

namespace Vigia.Agente.Infra;

public static class FoundryChatClientFactory
{
    // Local: login do az CLI / Visual Studio. No Container Apps: Managed Identity.
    private static readonly DefaultAzureCredential Credencial = new();

    public static IChatClient Criar(string endpoint, string deployment)
    {
        var politica = new BearerTokenPolicy(Credencial, "https://ai.azure.com/.default");

#pragma warning disable OPENAI001 // construtor com AuthenticationPolicy ainda marcado como experimental
        var chatClient = new ChatClient(
            model: deployment,
            authenticationPolicy: politica,
            options: new OpenAIClientOptions
            {
                Endpoint = new Uri(new Uri(endpoint), "openai/v1/"),
                // O SDK já tenta de novo com backoff e respeita o retry-after. 3 é o padrão.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 3)
            });
#pragma warning restore OPENAI001

        return chatClient.AsIChatClient();
    }

    /// <summary>Retorna o tempo de espera quando a falha é cota (HTTP 429). Qualquer outra falha: null.</summary>
    public static TimeSpan? ClassificarCota(Exception ex)
    {
        if (ex is not ClientResultException { Status: 429 } erro)
            return null;

        var cabecalhos = erro.GetRawResponse()?.Headers;

        if (cabecalhos?.TryGetValue("retry-after-ms", out var ms) == true && double.TryParse(ms, out var milissegundos))
            return TimeSpan.FromMilliseconds(milissegundos);

        if (cabecalhos?.TryGetValue("retry-after", out var s) == true && double.TryParse(s, out var segundos))
            return TimeSpan.FromSeconds(segundos);

        return TimeSpan.FromMinutes(1);
    }
}
