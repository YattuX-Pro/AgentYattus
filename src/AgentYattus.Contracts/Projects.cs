using System.Text.Json.Serialization;

namespace AgentYattus.Contracts;

/// <summary>Ce qu'une personne peut faire dans un projet (ADR 0009).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectRole>))]
public enum ProjectRole
{
    /// <summary>Le propriétaire : tout, y compris partager.</summary>
    Owner,

    /// <summary>Lance des missions et décide des écritures.</summary>
    Editor,

    /// <summary>Voit les missions et leurs rapports.</summary>
    Reader,
}

public sealed record ProjectMemberDto(string UserId, string Username, string? DisplayName, ProjectRole Role);

/// <param name="MyAccess">Ce que l'appelant peut faire ; Reader pour un administrateur de l'instance qui n'en est pas membre.</param>
/// <param name="CanManage">Vrai si l'appelant peut renommer, changer la visibilité et inviter (propriétaire, admin de l'organisation).</param>
/// <param name="Workspace">Le dossier de travail propre au projet ; null : le dossier configuré par défaut.</param>
public sealed record ProjectDto(
    Guid Id, string Name, Guid? OrganizationId, string? OrganizationName, UserRefDto Owner, ProjectVisibility Visibility,
    bool IsPersonalDefault, string? Workspace, ProjectRole MyAccess, bool CanManage, IReadOnlyList<ProjectMemberDto> Members,
    DateTimeOffset CreatedAt);

/// <param name="OrganizationId">Null : le projet va dans l'espace personnel de son créateur.</param>
/// <param name="Visibility">Null : la visibilité par défaut de l'organisation, ou Privé.</param>
public sealed record CreateProjectRequest(string Name, Guid? OrganizationId = null, ProjectVisibility? Visibility = null);

public sealed record UpdateProjectRequest(string Name, ProjectVisibility Visibility);

/// <param name="Role">Editor ou Reader.</param>
public sealed record SetProjectMemberRequest(ProjectRole Role);

/// <param name="Path">Chemin absolu d'un dossier existant sur le serveur ; null pour revenir au dossier par défaut.</param>
public sealed record SetWorkspaceRequest(string? Path);

public static class ProjectsProtocol
{
    public const string Path = "/projects";

    public static string Members(Guid project, string userId) => $"/projects/{project}/members/{Uri.EscapeDataString(userId)}";

    public static string Workspace(Guid project) => $"/projects/{project}/workspace";
}
