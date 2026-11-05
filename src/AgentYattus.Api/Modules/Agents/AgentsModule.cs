namespace AgentYattus.Api.Modules.Agents;

public interface IAgentRunner
{
    Task RunAsync(Guid missionId, string goal, CancellationToken cancellationToken);
}

public static class AgentsModule
{
    public static IServiceCollection AddAgentsModule(this IServiceCollection services) =>
        services.AddScoped<IAgentRunner, PlaceholderAgentRunner>();
}

// Le vrai exécuteur arrive en phase 2 de la feuille de route : Agent Framework,
// avec les outils de spikes/AgentLoop/WorkspaceTools.cs.
internal sealed class PlaceholderAgentRunner : IAgentRunner
{
    public Task RunAsync(Guid missionId, string goal, CancellationToken cancellationToken) => Task.CompletedTask;
}
