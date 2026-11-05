using System.Text.Json.Serialization;

namespace AgentYattus.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<MissionStatus>))]
public enum MissionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}

public sealed record CreateMissionRequest(string Goal);

public sealed record MissionDto(Guid Id, string Goal, MissionStatus Status, DateTimeOffset CreatedAt);

public sealed record MissionEventDto(Guid MissionId, MissionStatus Status, DateTimeOffset At);

public static class MissionsHubProtocol
{
    public const string Path = "/hubs/missions";
    public const string MissionEvent = "MissionEvent";
}
