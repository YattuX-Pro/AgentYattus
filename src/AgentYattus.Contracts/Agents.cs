namespace AgentYattus.Contracts;

/// <param name="Role">Ce que fait l'agent, en une phrase.</param>
public sealed record AgentDto(string Name, string Role, string Model, int MaxIterations, IReadOnlyList<ToolInfoDto> Tools);

/// <param name="RequiresApproval">Vrai si chaque appel attend une décision humaine.</param>
public sealed record ToolInfoDto(string Name, string Description, bool RequiresApproval);

public static class AgentsProtocol
{
    public const string Path = "/agents";
}
