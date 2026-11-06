using System.Text.Json.Serialization;

namespace AgentYattus.Contracts;

/// <summary>Le rôle d'un membre dans une organisation (ADR 0009).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrganizationRole>))]
public enum OrganizationRole
{
    /// <summary>Gère l'organisation et ses membres ; éditeur de tous ses projets.</summary>
    Admin,

    /// <summary>Crée des projets dans l'organisation.</summary>
    Operator,

    /// <summary>Voit les projets ouverts à l'organisation.</summary>
    Observer,
}

/// <summary>Qui accède à un projet.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectVisibility>))]
public enum ProjectVisibility
{
    /// <summary>Le propriétaire seul.</summary>
    Private,

    /// <summary>Le propriétaire et ses invités.</summary>
    Shared,

    /// <summary>En plus, tous les membres de l'organisation, en lecture.</summary>
    Organization,
}

public sealed record MembershipDto(Guid OrganizationId, string Name, string Slug, OrganizationRole Role);

/// <summary>L'utilisateur connecté, tel que l'application le connaît.</summary>
public sealed record MeDto(string Id, string Username, string? DisplayName, bool IsInstanceAdmin, IReadOnlyList<MembershipDto> Organizations);

/// <summary>Une personne de l'annuaire (elle s'est connectée au moins une fois).</summary>
public sealed record UserDto(
    string Id, string Username, string? DisplayName, bool IsInstanceAdmin,
    DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, IReadOnlyList<MembershipDto> Organizations);

/// <summary>Le minimum pour désigner quelqu'un (recherche, listes de membres).</summary>
public sealed record UserRefDto(string Id, string Username, string? DisplayName);

public sealed record MemberDto(string UserId, string Username, string? DisplayName, OrganizationRole Role);

/// <param name="MyRole">Le rôle de l'appelant ; null s'il n'est pas membre (administrateur de l'instance).</param>
public sealed record OrganizationDto(
    Guid Id, string Name, string Slug, ProjectVisibility DefaultVisibility, IReadOnlyList<MemberDto> Members, OrganizationRole? MyRole);

public sealed record CreateOrganizationRequest(string Name, string Slug);

public sealed record UpdateOrganizationRequest(string Name, ProjectVisibility DefaultVisibility);

public sealed record SetMemberRequest(OrganizationRole Role);

public static class IdentityProtocol
{
    public const string Me = "/me";
    public const string Users = "/users";
    public const string Lookup = "/users/lookup";
    public const string Organizations = "/organizations";

    public static string Members(Guid organization, string userId) => $"/organizations/{organization}/members/{Uri.EscapeDataString(userId)}";
}
