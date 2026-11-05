using Anthropic;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Modules.Agents;

public interface IAgentRunner
{
    /// <param name="resume">La conversation mise en pause et la décision humaine, ou null pour un premier passage.</param>
    Task<AgentOutcome> RunAsync(Guid missionId, string goal, AgentResume? resume, CancellationToken cancellationToken);
}

/// <summary>
/// Soit l'agent a fini (<see cref="Answer"/>), soit il attend une décision humaine (<see cref="Approval"/>).
/// Dans ce cas, <see cref="Conversation"/> est l'état à conserver tel quel pour reprendre plus tard.
/// </summary>
public sealed record AgentOutcome(string? Answer, AgentApproval? Approval = null, string? Conversation = null);

/// <summary>Les actions que l'agent veut faire et qui attendent une décision humaine.</summary>
public sealed record AgentApproval(IReadOnlyList<AgentToolCall> ToolCalls);

public sealed record AgentToolCall(string Tool, string Arguments);

public sealed record AgentResume(string Conversation, bool Approved);

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
