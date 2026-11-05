using Anthropic;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Agents;

public interface IAgentRunner
{
    /// <returns>La réponse finale de l'agent.</returns>
    Task<string?> RunAsync(Guid missionId, string goal, CancellationToken cancellationToken);
}

public static class AgentsModule
{
    public static IServiceCollection AddAgentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AgentsOptions>().BindConfiguration(AgentsOptions.Section);
        services.AddScoped<IAgentRunner, AgentFrameworkRunner>();

        // Sans clé, l'API démarre quand même (santé, lecture des missions) ; ce sont les missions qui échouent.
        if (configuration["Anthropic:ApiKey"] is { Length: > 0 } apiKey)
        {
            var model = configuration[$"{AgentsOptions.Section}:Model"] ?? AgentsOptions.DefaultModel;
            services.AddSingleton<IChatClient>(_ =>
                new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model, defaultMaxOutputTokens: 16_000));
        }

        return services;
    }
}
