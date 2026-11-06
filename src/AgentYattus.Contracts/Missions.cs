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
}

/// <summary>Question : l'Analyste répond. Revue : l'Analyste rend ses constats, le Rédacteur écrit REPORT.md.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissionKind>))]
public enum MissionKind
{
    Question,
    Review,
}

/// <param name="ProjectId">Le projet de la mission ; null : le projet personnel de l'auteur (ADR 0009).</param>
public sealed record CreateMissionRequest(string Goal, MissionKind Kind = MissionKind.Question, Guid? ProjectId = null);

/// <param name="Result">La réponse finale de l'agent, une fois la mission terminée.</param>
/// <param name="PendingApproval">Les actions qui attendent une décision, quand le statut est AwaitingApproval.</param>
/// <param name="ProjectId">Null pour une mission antérieure aux projets (visible des seuls administrateurs de l'instance).</param>
/// <param name="CreatedBy">Le nom d'utilisateur de l'auteur.</param>
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
    string? CreatedBy = null);

/// <param name="Id">Identifie cette demande : une décision ne vaut que pour la demande que l'on a vue.</param>
public sealed record ApprovalDto(Guid Id, IReadOnlyList<ToolCallDto> ToolCalls);

/// <param name="Arguments">Les arguments de l'outil, en JSON : exactement ce qui sera exécuté si on approuve.</param>
public sealed record ToolCallDto(string Tool, string Arguments);

/// <param name="ApprovalId">La demande affichée au moment de décider (<see cref="ApprovalDto.Id"/>).</param>
public sealed record DecisionRequest(bool Approved, Guid ApprovalId);

public sealed record MissionEventDto(Guid MissionId, MissionStatus Status, DateTimeOffset At);

/// <param name="Type">Ce qui s'est passé : un statut (« Running »), un outil (« outil:read_file »), une décision…</param>
public sealed record AuditEntryDto(string Type, DateTimeOffset At);

/// <summary>Une ligne du journal de toutes les missions, pour l'espace admin.</summary>
public sealed record AuditFeedEntryDto(Guid MissionId, string Type, DateTimeOffset At);

public static class AuditProtocol
{
    public const string Path = "/audit";
}

public static class MissionsHubProtocol
{
    public const string Path = "/hubs/missions";
    public const string MissionEvent = "MissionEvent";
}
