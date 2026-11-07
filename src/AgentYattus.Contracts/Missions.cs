using System.Text.Json.Serialization;

namespace AgentYattus.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<MissionStatus>))]
public enum MissionStatus
{
    Pending,
    Running,
    Completed,
    Failed,

    /// <summary>L'agent attend une décision humaine avant une action sensible.</summary>
    AwaitingApproval,

    /// <summary>Arrêtée par un humain, en file, en cours ou en pause.</summary>
    Cancelled,
}

/// <summary>Question : l'Analyste répond. Revue : l'Analyste rend ses constats, le Rédacteur écrit REPORT.md.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissionKind>))]
public enum MissionKind
{
    Question,
    Review,
}

/// <param name="ProjectId">Le projet de la mission ; null : le projet personnel de l'auteur (ADR 0009).</param>
/// <param name="Agent">L'agent qui fait une mission « Question » (ADR 0013) ; null : l'Analyste.</param>
public sealed record CreateMissionRequest(string Goal, MissionKind Kind = MissionKind.Question, Guid? ProjectId = null, string? Agent = null);

/// <param name="Result">La réponse finale de l'agent, une fois la mission terminée.</param>
/// <param name="PendingApproval">Les actions qui attendent une décision, quand le statut est AwaitingApproval.</param>
/// <param name="ProjectId">Null pour une mission antérieure aux projets (visible des seuls administrateurs de l'instance).</param>
/// <param name="CreatedBy">Le nom d'utilisateur de l'auteur.</param>
/// <param name="Cost">Coût estimé en dollars, au prix du catalogue (ADR 0010).</param>
/// <param name="RetryOf">La mission dont celle-ci est la relance.</param>
public sealed record MissionDto(
    Guid Id,
    string Goal,
    MissionStatus Status,
    DateTimeOffset CreatedAt,
    string? Result = null,
    ApprovalDto? PendingApproval = null,
    MissionKind Kind = MissionKind.Question,
    Guid? ProjectId = null,
    string? ProjectName = null,
    string? CreatedBy = null,
    long InputTokens = 0,
    long OutputTokens = 0,
    decimal Cost = 0,
    Guid? RetryOf = null,
    string? Agent = null);

/// <param name="Id">Identifie cette demande : une décision ne vaut que pour la demande que l'on a vue.</param>
public sealed record ApprovalDto(Guid Id, IReadOnlyList<ToolCallDto> ToolCalls);

/// <param name="Arguments">Les arguments de l'outil, en JSON : exactement ce qui sera exécuté si on approuve.</param>
public sealed record ToolCallDto(string Tool, string Arguments);

/// <param name="ApprovalId">La demande affichée au moment de décider (<see cref="ApprovalDto.Id"/>).</param>
public sealed record DecisionRequest(bool Approved, Guid ApprovalId);

/// <summary>Répondre à l'agent après sa réponse (leçon 117) : la mission reprend, avec toute sa conversation.</summary>
public sealed record ReplyRequest(string Text);

/// <summary>Un tour de la conversation, après le but : une réponse de l'agent, ou un message de toi.</summary>
public sealed record MissionTurnDto(bool FromUser, string Text, DateTimeOffset At);

public sealed record MissionEventDto(Guid MissionId, MissionStatus Status, DateTimeOffset At);

/// <param name="Type">Ce qui s'est passé : un statut (« Running »), un outil (« outil:read_file »), une décision…</param>
public sealed record AuditEntryDto(string Type, DateTimeOffset At);

/// <summary>Une ligne du journal de toutes les missions, pour l'espace admin.</summary>
public sealed record AuditFeedEntryDto(Guid MissionId, string Type, DateTimeOffset At);

public static class AuditProtocol
{
    public const string Path = "/audit";
    public const string Export = "/audit/export";
}

public static class MissionsHubProtocol
{
    public const string Path = "/hubs/missions";
    public const string MissionEvent = "MissionEvent";
}
