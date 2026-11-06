namespace AgentYattus.Contracts;

/// <param name="Role">Ce que fait l'agent, en une phrase.</param>
/// <param name="Model">Le modèle qu'il utilisera (« fournisseur/modèle »).</param>
/// <param name="Key">Son identifiant (« analyste »).</param>
/// <param name="ModelOverride">Son modèle propre ; null s'il utilise le modèle par défaut.</param>
public sealed record AgentDto(
    string Name, string Role, string Model, int MaxIterations, IReadOnlyList<ToolInfoDto> Tools,
    string Key = "", LlmSelectionDto? ModelOverride = null);

/// <param name="Model">Null pour revenir au modèle par défaut.</param>
public sealed record UpdateAgentModelRequest(LlmSelectionDto? Model);

/// <param name="RequiresApproval">Vrai si chaque appel attend une décision humaine.</param>
public sealed record ToolInfoDto(string Name, string Description, bool RequiresApproval);

public static class AgentsProtocol
{
    public const string Path = "/agents";

    public static string Model(string role) => $"/agents/{role}/model";
}
