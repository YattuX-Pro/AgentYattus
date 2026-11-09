namespace AgentYattus.Contracts;

/// <summary>Fiche du RH ; la création passe ensuite par les contrôles du recrutement manuel.</summary>
public sealed record RecruitmentDraftDto(
    string Name, string Summary, string Instructions, IReadOnlyList<string> Tools, IReadOnlyList<RecruitmentSkillDto> Skills);

public sealed record RecruitmentSkillDto(string Name, string Description, string Body);

/// <summary>Réponse lisible et, lorsqu'il est prêt, profil structuré d'une mission RH.</summary>
public sealed record RecruitmentResultDto(string Message, RecruitmentDraftDto? Draft, bool Created = false, Guid? RequestId = null);
