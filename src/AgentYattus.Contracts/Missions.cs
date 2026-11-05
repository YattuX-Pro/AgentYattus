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

public sealed record ApprovalDto(IReadOnlyList<ToolCallDto> ToolCalls);

/// <param name="Arguments">Les arguments de l'outil, en JSON : exactement ce qui sera exécuté si on approuve.</param>
public sealed record ToolCallDto(string Tool, string Arguments);

public sealed record DecisionRequest(bool Approved);

public sealed record MissionEventDto(Guid MissionId, MissionStatus Status, DateTimeOffset At);

public static class MissionsHubProtocol
{
    public const string Path = "/hubs/missions";
    public const string MissionEvent = "MissionEvent";
}
