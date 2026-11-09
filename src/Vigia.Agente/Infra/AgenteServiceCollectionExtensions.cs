using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Vigia.Agente.Agente;

namespace Vigia.Agente.Infra;

public static class AgenteServiceCollectionExtensions
{
    public static IServiceCollection AddAgenteDePlantao(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<VigiaOptions>(configuration.GetSection("Vigia"));
        services.Configure<FoundryOptions>(configuration.GetSection("Foundry"));
        services.Configure<NotificacaoOptions>(configuration.GetSection("Notificacao"));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RegistroDeIncidentes>();
        services.AddSingleton<AprovacoesPendentes>();
        services.AddSingleton<OrcamentoDeTokens>();
        services.AddSingleton<EstadoDoAgente>();
        services.AddHttpClient();
        services.AddHttpClient<FonteDeSinais>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<VigiaOptions>>().Value.UrlDaAplicacao);
            http.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddChatClient(sp =>
            {
                var opcoes = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;

                if (string.IsNullOrWhiteSpace(opcoes.Endpoint))
                    return new ChatClientSimulado();

                var reserva = string.IsNullOrWhiteSpace(opcoes.EndpointReserva)
                    ? null
                    : FoundryChatClientFactory.Criar(opcoes.EndpointReserva, opcoes.DeploymentReserva);

                return new ChatClientResiliente(
                    FoundryChatClientFactory.Criar(opcoes.Endpoint, opcoes.Deployment),
                    reserva,
                    FoundryChatClientFactory.ClassificarCota,
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILogger<ChatClientResiliente>>());
            })
            // Executa as ferramentas pedidas pelo modelo, com limite de voltas para não entrar em loop
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = 6)
            .UseLogging();

        services.AddHostedService<VigiaWorker>();
        services.AddHealthChecks().AddCheck<AgenteVivoHealthCheck>("agente-vivo");

        return services;
    }
}
