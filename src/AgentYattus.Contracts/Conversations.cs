namespace AgentYattus.Contracts;

/// <summary>Identité au moment du message ; ne dépend pas de la composition actuelle de l'équipe.</summary>
public sealed record MessageAuthorDto(string Role, string Name, Guid? ColleagueId = null, Guid? PortraitId = null, double MouthX = 0.56, double MouthY = 0.37);
public sealed record MissionActivityDto(Guid MissionId, Guid ExecutionId, MessageAuthorDto Author, string Text);
public sealed record ConversationNoticeDto(Guid Id, Guid MissionId, Guid? ProjectId, string? ProjectName,
    MissionStatus Status, MessageAuthorDto? Author, string Text, DateTimeOffset At, long? TurnId = null);
public sealed record ConversationDraftDto(string Text);

public sealed record AcceptResultRequest(long TurnId);

public sealed record ProjectGroupMessageDto(Guid MissionId, long TurnId, bool FromUser, string Text,
    DateTimeOffset At, MessageAuthorDto? Author, string? Recipient = null);
