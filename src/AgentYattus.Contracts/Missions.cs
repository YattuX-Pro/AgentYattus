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

public sealed record CreateMissionRequest(string Goal);

/// <param name="Result">La réponse finale de l'agent, une fois la mission terminée.</param>
/// <param name="PendingApproval">Les actions qui attendent une décision, quand le statut est AwaitingApproval.</param>
public sealed record MissionDto(
    Guid Id,
    string Goal,
    MissionStatus Status,
    DateTimeOffset CreatedAt,
    string? Result = null,
    ApprovalDto? PendingApproval = null);

/// <param name="Id">Identifie cette demande : une décision ne vaut que pour la demande que l'on a vue.</param>
public sealed record ApprovalDto(Guid Id, IReadOnlyList<ToolCallDto> ToolCalls);

/// <param name="Arguments">Les arguments de l'outil, en JSON : exactement ce qui sera exécuté si on approuve.</param>
public sealed record ToolCallDto(string Tool, string Arguments);

/// <param name="ApprovalId">La demande affichée au moment de décider (<see cref="ApprovalDto.Id"/>).</param>
public sealed record DecisionRequest(bool Approved, Guid ApprovalId);

public sealed record MissionEventDto(Guid MissionId, MissionStatus Status, DateTimeOffset At);

/// <param name="Type">Ce qui s'est passé : un statut (« Running »), un outil (« outil:read_file »), une décision…</param>
public sealed record AuditEntryDto(string Type, DateTimeOffset At);

public static class MissionsHubProtocol
{
    public const string Path = "/hubs/missions";
    public const string MissionEvent = "MissionEvent";
}
