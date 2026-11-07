namespace AgentYattus.Contracts;

// Les collègues (ADR 0012) : la galerie de portraits, l'équipe d'un projet, et ce que la scène du bureau affiche.

/// <param name="Slug">Identifiant lisible et stable (« ines »), utilisé par l'import et l'équipe de départ.</param>
/// <param name="WorkUrl">Pose « travaille », relative à l'API.</param>
/// <param name="TalkUrl">Pose « parle » ; la pose « travaille » si le portrait n'en a pas.</param>
public sealed record PortraitDto(Guid Id, string Slug, string SuggestedName, bool Active, bool HasTalk, string WorkUrl, string TalkUrl);

public sealed record UpdatePortraitRequest(string SuggestedName, bool Active);

/// <param name="Key">Identifiant stable (« rigueur ») ; c'est lui qu'un collègue garde.</param>
/// <param name="Style">La phrase ajoutée à la consigne de l'agent.</param>
public sealed record TraitDto(string Key, string Label, string Style);

public sealed record UpdateTraitsRequest(IReadOnlyList<TraitDto> Traits);

/// <summary>Une réplique : ce qu'un collègue dit quand un événement survient.</summary>
/// <param name="Event">Le type d'événement (« outil:read_file », « mission:fin »…).</param>
/// <param name="Speaker">Qui parle : « chef », « analyste », « redacteur ».</param>
/// <param name="Variables">Les variables utilisables, sans accolades (« fichier »).</param>
/// <param name="IsDefault">Vrai si personne ne l'a modifiée.</param>
public sealed record SceneLineDto(string Event, string Description, string Speaker, string Template, IReadOnlyList<string> Variables, bool IsDefault);

/// <param name="Templates">Par événement ; null ou vide : revenir à la réplique de départ.</param>
public sealed record UpdateSceneLinesRequest(IReadOnlyDictionary<string, string?> Templates);

public sealed record DefaultSeatDto(string Role, Guid PortraitId, string Name);

/// <param name="LeaderName">Le chef d'équipe : il annonce le début et la fin des missions.</param>
/// <param name="ManagerPortraitId">Le portrait de « Vous », dans le bureau vitré.</param>
/// <param name="TypingMs">Millisecondes par lettre quand une bulle s'écrit.</param>
/// <param name="PauseMs">Temps de lecture d'une bulle avant la suivante.</param>
public sealed record TeamSettingsDto(
    Guid? LeaderPortraitId, string LeaderName, Guid? ManagerPortraitId, IReadOnlyList<DefaultSeatDto> DefaultTeam, int TypingMs, int PauseMs);

public sealed record ColleagueDto(
    Guid Id, Guid ProjectId, string Role, Guid PortraitId, string Name, IReadOnlyList<string> Traits,
    UserRefDto? RecruitedBy, DateTimeOffset RecruitedAt, bool Active, DateTimeOffset? RemovedAt, IReadOnlyList<string>? Skills = null);

/// <summary>Pour la page Équipes de l'admin : un collègue et son projet.</summary>
public sealed record ColleagueEntryDto(ColleagueDto Colleague, string ProjectName);

/// <summary>Tout ce que le bureau doit savoir pour jouer la scène d'un projet, en un appel.</summary>
/// <param name="Lines">Par événement, la réplique en vigueur.</param>
public sealed record ProjectTeamDto(
    Guid ProjectId, IReadOnlyList<ColleagueDto> Colleagues, string LeaderName, Guid? LeaderPortraitId, Guid? ManagerPortraitId,
    int TypingMs, int PauseMs, IReadOnlyDictionary<string, string> Lines, bool CanRecruit);

/// <param name="Skills">Les compétences du collègue (clés de /skills) : dix au plus.</param>
public sealed record RecruitRequest(string Role, Guid PortraitId, string Name, IReadOnlyList<string> Traits, IReadOnlyList<string>? Skills = null);

public static class TeamProtocol
{
    public const string Portraits = "/team/portraits";
    public const string Traits = "/team/traits";
    public const string Lines = "/team/lines";
    public const string Settings = "/team/settings";
    public const string Colleagues = "/team/colleagues";

    /// <summary>Les deux poses d'un portrait : « travail » et « parle ».</summary>
    public static readonly IReadOnlyList<string> Poses = ["travail", "parle"];

    public static string Portrait(Guid id) => $"{Portraits}/{id}";
    public static string Image(Guid id, string pose) => $"{Portraits}/{id}/{pose}";
    public static string ProjectTeam(Guid project) => $"/projects/{project}/team";
    public static string ProjectColleagues(Guid project) => $"/projects/{project}/colleagues";
    public static string Colleague(Guid project, Guid colleague) => $"/projects/{project}/colleagues/{colleague}";
}
