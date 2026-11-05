using AgentYattus.Api.Modules.Llm;
using AgentYattus.Contracts;
using Microsoft.Extensions.Options;

namespace AgentYattus.Api.Modules.Agents;

public interface IAgentRunner
{
    /// <param name="resume">La conversation mise en pause et la décision humaine, ou null pour un premier passage.</param>
    Task<AgentOutcome> RunAsync(Guid missionId, string goal, MissionKind kind, AgentResume? resume, CancellationToken cancellationToken);
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
        // Le modèle et sa clé viennent du module Llm (choisis dans l'application) ; voir ILlmClientProvider.
        return services.AddScoped<IAgentRunner, AgentFrameworkRunner>();
    }

    public static IEndpointRouteBuilder MapAgentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AgentsProtocol.Path, async (IOptions<AgentsOptions> options, ILlmClientProvider llm, CancellationToken cancellationToken) =>
        {
            var model = await llm.GetModelAsync(cancellationToken);
            AgentDto Describe(AgentRole role) => new(role.Name, role.Summary, model, options.Value.MaxIterations,
            [
                .. WorkspaceTools.Catalog.Where(t => role.Tools.Contains(t.Name))
                    .Select(t => new ToolInfoDto(t.Name, t.Description, t.RequiresApproval)),
            ]);
            return TypedResults.Ok(AgentRoles.All.Select(Describe).ToArray());
        });
        return app;
    }
}
