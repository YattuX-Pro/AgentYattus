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
    bool Builtin = true, bool Mine = false);

/// <summary>Un agent créé dans l'admin (ADR 0013) : ses outils sont pris dans la liste existante.</summary>
/// <param name="Personal">Un agent à soi (depuis « Nouveau collègue ») : visible de son auteur seul. Toujours vrai pour un non-administrateur.</param>
/// <param name="Model">Son modèle ; null : celui de l'instance.</param>
/// <param name="BasedOn">Une consigne vide reprend celle de ce poste (un Analyste avec d'autres outils, par exemple).</param>
public sealed record CreateAgentRequest(
    string Name, string Summary, string Instructions, IReadOnlyList<string> Tools,
    bool Personal = false, LlmSelectionDto? Model = null, string? BasedOn = null);

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

/// <summary>Ce que les missions disent d'un agent : combien ont abouti, et ce qu'elles coûtent en moyenne.</summary>
/// <param name="Finished">Missions terminées où l'agent a travaillé : réussies, échouées ou arrêtées.</param>
/// <param name="Completed">Celles qui ont abouti.</param>
/// <param name="AverageCost">Coût moyen d'une de ces missions, en dollars, au prix du catalogue.
/// Une revue compte pour l'Analyste et pour le Rédacteur : c'est le coût de la mission entière.</param>
public sealed record AgentPerformanceDto(string Role, int Finished, int Completed, decimal AverageCost);

/// <param name="RequiresApproval">Vrai si chaque appel attend une décision humaine.</param>
public sealed record ToolInfoDto(string Name, string Description, bool RequiresApproval);

public static class AgentsProtocol
{
    public const string Path = "/agents";

    /// <summary>Tous les outils qu'on peut donner à un agent (« Nouveau collègue »).</summary>
    public const string Tools = "/agents/tools";

    /// <summary>Réussite et coût des missions de chaque agent (calculés par le module Missions).</summary>
    public const string Performance = "/agents/performance";

    public static string Model(string role) => $"/agents/{role}/model";

    public static string Role(string role) => $"/agents/{role}";

    public static string Profile(string role) => $"/agents/{role}/profile";

    public static string Versions(string role) => $"/agents/{role}/versions";
}
