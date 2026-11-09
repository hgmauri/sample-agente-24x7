using Microsoft.Extensions.AI;

namespace Vigia.Agente.Infra;

public sealed class CotaEsgotadaException(TimeSpan tentarApos, Exception inner)
    : Exception("Cota do modelo esgotada em todos os deployments.", inner)
{
    public TimeSpan TentarApos { get; } = tentarApos;
}

/// <summary>
/// Quando o deployment principal responde 429 (cota), este cliente:
/// 1. respeita o tempo de espera informado pelo Foundry (retry-after),
/// 2. desvia para um deployment reserva (outra região ou assinatura) enquanto isso,
/// 3. sem reserva, avisa o agente com CotaEsgotadaException para ele seguir sem IA.
/// O SDK da OpenAI já faz retry com backoff antes de chegar aqui; esta camada trata o que sobra.
/// </summary>
public sealed class ChatClientResiliente(
    IChatClient principal,
    IChatClient? reserva,
    Func<Exception, TimeSpan?> classificarCota,
    TimeProvider relogio,
    ILogger<ChatClientResiliente> logger) : IChatClient
{
    private DateTimeOffset _principalBloqueadoAte = DateTimeOffset.MinValue;
    private DateTimeOffset _reservaBloqueadaAte = DateTimeOffset.MinValue;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var agora = relogio.GetUtcNow();
        Exception? ultimaFalha = null;
        TimeSpan espera = TimeSpan.FromMinutes(1);

        if (agora >= _principalBloqueadoAte)
        {
            try
            {
                return await principal.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception ex) when (classificarCota(ex) is { } tempo)
            {
                _principalBloqueadoAte = agora + tempo;
                (ultimaFalha, espera) = (ex, tempo);
                logger.LogWarning("Deployment principal com cota esgotada. Bloqueado por {Tempo}", tempo);
            }
        }

        if (reserva is not null && agora >= _reservaBloqueadaAte)
        {
            try
            {
                logger.LogInformation("Usando deployment reserva");
                return await reserva.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception ex) when (classificarCota(ex) is { } tempo)
            {
                _reservaBloqueadaAte = agora + tempo;
                (ultimaFalha, espera) = (ex, TimeSpan.FromTicks(Math.Min(tempo.Ticks, espera.Ticks)));
            }
        }

        var liberaEm = new[] { _principalBloqueadoAte, reserva is null ? DateTimeOffset.MaxValue : _reservaBloqueadaAte }.Min();
        throw new CotaEsgotadaException(liberaEm > agora ? liberaEm - agora : espera, ultimaFalha ?? new InvalidOperationException("Deployments bloqueados"));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        principal.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : principal.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        principal.Dispose();
        reserva?.Dispose();
    }
}
