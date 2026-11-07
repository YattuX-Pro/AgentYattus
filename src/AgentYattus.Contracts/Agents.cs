namespace AgentYattus.Contracts;

/// <param name="Role">Ce que fait l'agent, en une phrase.</param>
/// <param name="Model">Le modèle qu'il utilisera (« fournisseur/modèle »).</param>
/// <param name="Key">Son identifiant (« analyste »).</param>
/// <param name="ModelOverride">Son modèle propre ; null s'il utilise le modèle par défaut.</param>
/// <param name="Instructions">La consigne en vigueur (partie modifiable).</param>
/// <param name="DefaultInstructions">La consigne d'origine, écrite dans le code.</param>
/// <param name="Guardrails">Ce que le code ajoute toujours à la fin, hors de portée de l'édition (ADR 0011).</param>
/// <param name="MaxIterationsOverride">La limite propre au rôle ; null : celle de l'instance.</param>
/// <param name="Builtin">Vrai pour l'Analyste et le Rédacteur (écrits dans le code) ; faux pour un agent créé dans l'admin (ADR 0013).</param>
public sealed record AgentDto(
    string Name, string Role, string Model, int MaxIterations, IReadOnlyList<ToolInfoDto> Tools,
    string Key = "", LlmSelectionDto? ModelOverride = null, AgentStatsDto? Stats = null,
    string Instructions = "", string DefaultInstructions = "", string Guardrails = "", int Version = 0, int? MaxIterationsOverride = null,
    bool Builtin = true);

/// <summary>Un agent créé dans l'admin (ADR 0013) : ses outils sont pris dans la liste existante.</summary>
public sealed record CreateAgentRequest(string Name, string Summary, string Instructions, IReadOnlyList<string> Tools);

public sealed record UpdateAgentProfileRequest(string Name, string Summary, IReadOnlyList<string> Tools);

/// <param name="Instructions">Null : revenir à la consigne d'origine.</param>
/// <param name="MaxIterations">Null : la limite de l'instance.</param>
public sealed record UpdateAgentRequest(string? Instructions, int? MaxIterations);

/// <param name="Instructions">Null : la consigne d'origine.</param>
public sealed record AgentVersionDto(int Version, string? Instructions, int? MaxIterations, DateTimeOffset At, string By);

/// <summary>Ce que le journal dit d'un rôle : combien de fois il a travaillé, et ce qu'il a consommé.</summary>
public sealed record AgentStatsDto(int Runs, long TotalTokens, long AverageTokens);

/// <param name="Model">Null pour revenir au modèle par défaut.</param>
public sealed record UpdateAgentModelRequest(LlmSelectionDto? Model);

/// <param name="RequiresApproval">Vrai si chaque appel attend une décision humaine.</param>
public sealed record ToolInfoDto(string Name, string Description, bool RequiresApproval);

public static class AgentsProtocol
{
    public const string Path = "/agents";

    public static string Model(string role) => $"/agents/{role}/model";

    public static string Role(string role) => $"/agents/{role}";

    public static string Profile(string role) => $"/agents/{role}/profile";

    public static string Versions(string role) => $"/agents/{role}/versions";
}
