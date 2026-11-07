using AgentYattus.Api.Modules.Identity;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Projects;

/// <summary>Les droits sur les projets, pour les autres modules (Missions). Toujours pour l'utilisateur de la requête.</summary>
public interface IProjectAccess
{
    /// <summary>Ce que l'utilisateur peut faire dans ce projet ; null s'il n'y a pas accès (ou si le projet n'existe pas).</summary>
    Task<ProjectRole?> AccessAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Les projets lisibles par l'utilisateur ; null pour « tous » (administrateur de l'instance).</summary>
    Task<IReadOnlyList<Guid>?> ReadableAsync(CancellationToken cancellationToken);

    /// <summary>Le projet personnel de l'utilisateur, créé à la première demande.</summary>
    Task<Guid> PersonalProjectAsync(CancellationToken cancellationToken);
}

/// <summary>Ce que l'exécution d'une mission doit savoir d'un projet, hors de toute requête (le worker).</summary>
public interface IProjectDirectory
{
    /// <summary>Les personnes qui peuvent lire le projet : à elles seules, le direct envoie ses événements.</summary>
    Task<IReadOnlyList<string>> ReadersAsync(Guid projectId, CancellationToken cancellationToken);
    /// <summary>
    /// Le dossier où les agents travaillent pour ce projet. Sans dossier choisi, le dossier configuré du serveur
    /// (Agents:Workspace) ne sert qu'aux projets d'un administrateur de l'instance, qui l'a configuré : sinon,
    /// n'importe qui ferait lire ce dossier commun en créant un projet. Allowed = false : la mission doit échouer.
    /// </summary>
    Task<ProjectWorkspace> WorkspaceAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken);
}

/// <param name="Path">Le dossier du projet ; null avec <paramref name="Allowed"/> : le dossier configuré du serveur.</param>
public sealed record ProjectWorkspace(bool Allowed, string? Path);

public static class ProjectsModule
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services) =>
        services
            .AddScoped<IProjectAccess, ProjectAccess>()
            .AddScoped<IProjectDirectory, ProjectDirectory>()
            .AddModuleDb<ProjectsDb>(ProjectsDb.Schema);

    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup(ProjectsProtocol.Path);

        projects.MapGet("/", async (ICurrentUser me, IDirectory directory, ProjectsDb db, IProjectAccess access, CancellationToken ct) =>
        {
            // Le projet personnel existe dès la première visite : son équipe s'affiche avant toute mission.
            await access.PersonalProjectAsync(ct);
            var orgs = await directory.MembershipsAsync(me.Id, ct);
            var list = await ProjectAccess.Readable(db, me, orgs).OrderBy(p => p.Name).ToListAsync(ct);
            return TypedResults.Ok(await ToDtosAsync(db, directory, me, orgs, list, ct));
        });

        projects.MapGet("/{id:guid}", async Task<Results<Ok<ProjectDto>, NotFound>> (
            Guid id, ICurrentUser me, IDirectory directory, ProjectsDb db, CancellationToken ct) =>
        {
            var orgs = await directory.MembershipsAsync(me.Id, ct);
            var project = await db.Projects.FindAsync([id], ct);
            if (project is null || await ProjectAccess.LevelAsync(db, project, me, orgs, ct) is null)
                return TypedResults.NotFound();
            return TypedResults.Ok((await ToDtosAsync(db, directory, me, orgs, [project], ct))[0]);
        });

        projects.MapPost("/", async Task<Results<Created<ProjectDto>, NotFound, ForbidHttpResult, ValidationProblem>> (
            CreateProjectRequest request, ICurrentUser me, IDirectory directory, ProjectsDb db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80)
                return Invalid("name", "Le nom fait entre 2 et 80 caractères.");
            var orgs = await directory.MembershipsAsync(me.Id, ct);
            var visibility = request.Visibility ?? ProjectVisibility.Private;
            if (request.OrganizationId is { } orgId)
            {
                if (!orgs.TryGetValue(orgId, out var role))
                    return TypedResults.NotFound();
                if (role == OrganizationRole.Observer)
                    return TypedResults.Forbid();
                visibility = request.Visibility ?? await directory.DefaultVisibilityAsync(orgId, ct) ?? ProjectVisibility.Private;
            }
            else if (visibility == ProjectVisibility.Organization)
                return Invalid("visibility", "Un projet personnel ne peut pas être ouvert à une organisation.");
            if (!Enum.IsDefined(visibility))
                return Invalid("visibility", "Visibilité inconnue.");

            var project = new Project
            {
                Id = Guid.CreateVersion7(), Name = name, OrganizationId = request.OrganizationId, OwnerId = me.Id,
                Visibility = visibility, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"{ProjectsProtocol.Path}/{project.Id}", (await ToDtosAsync(db, directory, me, orgs, [project], ct))[0]);
        });

        projects.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid id, UpdateProjectRequest request, ICurrentUser me, IDirectory directory, ProjectsDb db, CancellationToken ct) =>
        {
            var (project, refusal) = await ManageableAsync(db, directory, me, id, ct);
            if (project is null)
                return refusal!;
            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80)
                return Invalid("name", "Le nom fait entre 2 et 80 caractères.");
            if (!Enum.IsDefined(request.Visibility))
                return Invalid("visibility", "Visibilité inconnue.");
            if (project.OrganizationId is null && request.Visibility == ProjectVisibility.Organization)
                return Invalid("visibility", "Un projet personnel ne peut pas être ouvert à une organisation.");
            project.Name = name;
            project.Visibility = request.Visibility;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        projects.MapPut("/{id:guid}/members/{userId}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid id, string userId, SetProjectMemberRequest request, ICurrentUser me, IDirectory directory, ProjectsDb db, CancellationToken ct) =>
        {
            var (project, refusal) = await ManageableAsync(db, directory, me, id, ct);
            if (project is null)
                return refusal!;
            if (request.Role is not (ProjectRole.Editor or ProjectRole.Reader))
                return Invalid("role", "Un invité est éditeur ou lecteur.");
            if (userId == project.OwnerId)
                return Invalid("userId", "C'est le propriétaire du projet.");
            if (!await directory.ExistsAsync(userId, ct))
                return TypedResults.NotFound();

            var member = await db.Members.FindAsync([id, userId], ct);
            if (member is null)
                db.Members.Add(new ProjectMember { ProjectId = id, UserId = userId, Role = request.Role, AddedAt = DateTimeOffset.UtcNow, AddedBy = me.Id });
            else
                member.Role = request.Role;
            // Inviter quelqu'un dans un projet privé : il devient partagé, sinon l'invitation ne servirait à rien.
            if (project.Visibility == ProjectVisibility.Private)
                project.Visibility = ProjectVisibility.Shared;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        projects.MapDelete("/{id:guid}/members/{userId}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid id, string userId, ICurrentUser me, IDirectory directory, ProjectsDb db, CancellationToken ct) =>
        {
            var (project, refusal) = await ManageableAsync(db, directory, me, id, ct);
            if (project is null)
                return refusal!;
            if (await db.Members.FindAsync([id, userId], ct) is not { } member)
                return TypedResults.NotFound();
            db.Members.Remove(member);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        // Où choisir : n'importe où pour l'administrateur (et en mode local, où l'on est administrateur), sinon sous les
        // dossiers autorisés par lui (ADR 0014).
        projects.MapGet("/workspace-roots", (ICurrentUser me, IConfiguration configuration) =>
            TypedResults.Ok(new WorkspaceRootsDto(me.IsInstanceAdmin, me.IsInstanceAdmin ? [] : AllowedRoots(configuration))));

        // Le dossier de travail : l'administrateur, n'importe lequel ; le gestionnaire du projet, sous un dossier autorisé.
        projects.MapPut("/{id:guid}/workspace", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid id, SetWorkspaceRequest request, ProjectsDb db, IDirectory directory, ICurrentUser me, IConfiguration configuration,
            CancellationToken ct) =>
        {
            var (project, refusal) = me.IsInstanceAdmin
                ? (await db.Projects.FindAsync([id], ct), null)
                : await ManageableAsync(db, directory, me, id, ct);
            if (refusal is { } refused)
                return refused;
            if (project is null)
                return TypedResults.NotFound();
            var path = string.IsNullOrWhiteSpace(request.Path) ? null : request.Path.Trim();
            if (path is not null && (path.Length > 500 || !Path.IsPathFullyQualified(path) || !System.IO.Directory.Exists(path)))
                return Invalid("path", "Le chemin doit être absolu et désigner un dossier existant sur le serveur.");
            if (path is not null && !me.IsInstanceAdmin)
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                var roots = AllowedRoots(configuration);
                // Un sous-dossier d'une racine autorisée, jamais la racine elle-même (elle contient les dossiers de tous).
                if (roots.Contains(full) || !InsideAllowedRoot(full, roots))
                    return Invalid("path", "Choisis un sous-dossier d'un des dossiers autorisés par l'administrateur.");
                // Ni le dossier d'un autre projet, ni un dossier qui le contient ou qu'il contient : on lirait ses fichiers.
                var others = await db.Projects.Where(p => p.Id != id && p.Workspace != null).Select(p => p.Workspace!).ToListAsync(ct);
                if (others.Any(other => Overlaps(full, Path.TrimEndingDirectorySeparator(other))))
                    return Invalid("path", "Ce dossier est déjà celui d'un autre projet, ou le contient.");
            }
            project.Workspace = path is null ? null : Path.GetFullPath(path);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        return app;
    }

    private static IReadOnlyList<string> AllowedRoots(IConfiguration configuration) =>
        [.. (configuration.GetSection("Projects:AllowedRoots").Get<string[]>() ?? [])
            .Where(r => Path.IsPathFullyQualified(r)).Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))];

    /// <summary>
    /// Dans un dossier autorisé, sans passer par un lien symbolique : sinon « autorise/lien → /etc » ferait lire /etc.
    /// La racine elle-même vient de la configuration de l'administrateur.
    /// </summary>
    internal static bool InsideAllowedRoot(string fullPath, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (fullPath != root && !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            var current = root;
            var linked = false;
            foreach (var part in Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar).Where(p => p != "."))
            {
                current = Path.Combine(current, part);
                linked |= new DirectoryInfo(current).LinkTarget is not null;
            }
            if (!linked)
                return true;
        }
        return false;
    }

    private static bool Overlaps(string a, string b) =>
        a == b || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.Ordinal) || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>Gérer un projet : son propriétaire, ou un administrateur de son organisation. Lecteur : 403 ; sans accès : 404.</summary>
    private static async Task<(Project? Project, Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>? Refusal)> ManageableAsync(
        ProjectsDb db, IDirectory directory, ICurrentUser me, Guid id, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([id], ct);
        if (project is null)
            return (null, TypedResults.NotFound());
        var orgs = await directory.MembershipsAsync(me.Id, ct);
        if (ProjectAccess.CanManage(project, me.Id, orgs))
            return (project, null);
        return (null, await ProjectAccess.LevelAsync(db, project, me, orgs, ct) is null ? TypedResults.NotFound() : TypedResults.Forbid());
    }

    private static async Task<List<ProjectDto>> ToDtosAsync(
        ProjectsDb db, IDirectory directory, ICurrentUser me, IReadOnlyDictionary<Guid, OrganizationRole> orgs, List<Project> projects, CancellationToken ct)
    {
        var ids = projects.Select(p => p.Id).ToList();
        var members = await db.Members.Where(m => ids.Contains(m.ProjectId)).ToListAsync(ct);
        var people = await directory.ResolveAsync(projects.Select(p => p.OwnerId).Concat(members.Select(m => m.UserId)), ct);
        var orgNames = await directory.OrganizationNamesAsync(projects.Where(p => p.OrganizationId is not null).Select(p => p.OrganizationId!.Value), ct);
        UserRefDto Ref(string id) => people.TryGetValue(id, out var u) ? u : new UserRefDto(id, id, null);

        return [.. projects.Select(p =>
        {
            var own = members.Where(m => m.ProjectId == p.Id).ToList();
            var orgRole = p.OrganizationId is { } o && orgs.TryGetValue(o, out var r) ? r : (OrganizationRole?)null;
            return new ProjectDto(
                p.Id, p.Name, p.OrganizationId, p.OrganizationId is { } oid && orgNames.TryGetValue(oid, out var n) ? n : null,
                Ref(p.OwnerId), p.Visibility, p.IsPersonalDefault, p.Workspace,
                ProjectAccess.Level(p, own, me.Id, orgRole, me.IsInstanceAdmin) ?? ProjectRole.Reader,
                ProjectAccess.CanManage(p, me.Id, orgs),
                [.. own.Select(m => { var u = Ref(m.UserId); return new ProjectMemberDto(u.Id, u.Username, u.DisplayName, m.Role); })],
                p.CreatedAt);
        })];
    }
}

internal sealed class ProjectAccess(ProjectsDb db, ICurrentUser me, IDirectory directory) : IProjectAccess
{
    /// <summary>
    /// La règle d'accès, en un seul endroit (ADR 0009) : propriétaire ; admin de l'organisation (éditeur) ;
    /// invité (si le projet n'est pas privé) ; membre de l'organisation (lecteur, si le projet lui est ouvert) ;
    /// administrateur de l'instance (lecteur).
    /// </summary>
    public static ProjectRole? Level(Project project, IReadOnlyCollection<ProjectMember> members, string userId, OrganizationRole? orgRole, bool instanceAdmin)
    {
        if (project.OwnerId == userId)
            return ProjectRole.Owner;
        if (project.OrganizationId is not null && orgRole == OrganizationRole.Admin)
            return ProjectRole.Editor;
        if (project.Visibility != ProjectVisibility.Private && members.FirstOrDefault(m => m.UserId == userId) is { } member)
            return member.Role;
        if (project.Visibility == ProjectVisibility.Organization && project.OrganizationId is not null && orgRole is not null)
            return ProjectRole.Reader;
        return instanceAdmin ? ProjectRole.Reader : null;
    }

    public static bool CanManage(Project project, string userId, IReadOnlyDictionary<Guid, OrganizationRole> orgs) =>
        project.OwnerId == userId
        || (project.OrganizationId is { } o && orgs.TryGetValue(o, out var role) && role == OrganizationRole.Admin);

    public static async Task<ProjectRole?> LevelAsync(
        ProjectsDb db, Project project, ICurrentUser me, IReadOnlyDictionary<Guid, OrganizationRole> orgs, CancellationToken ct)
    {
        var members = await db.Members.Where(m => m.ProjectId == project.Id).ToListAsync(ct);
        var orgRole = project.OrganizationId is { } o && orgs.TryGetValue(o, out var r) ? r : (OrganizationRole?)null;
        return Level(project, members, me.Id, orgRole, me.IsInstanceAdmin);
    }

    /// <summary>La même règle, traduite en requête SQL : les projets lisibles par l'utilisateur.</summary>
    public static IQueryable<Project> Readable(ProjectsDb db, ICurrentUser me, IReadOnlyDictionary<Guid, OrganizationRole> orgs)
    {
        if (me.IsInstanceAdmin)
            return db.Projects;
        var userId = me.Id;
        var myOrgs = orgs.Keys.ToList();
        var adminOf = orgs.Where(o => o.Value == OrganizationRole.Admin).Select(o => o.Key).ToList();
        return db.Projects.Where(p =>
            p.OwnerId == userId
            || (p.OrganizationId != null && adminOf.Contains(p.OrganizationId.Value))
            || (p.Visibility != ProjectVisibility.Private && db.Members.Any(m => m.ProjectId == p.Id && m.UserId == userId))
            || (p.Visibility == ProjectVisibility.Organization && p.OrganizationId != null && myOrgs.Contains(p.OrganizationId.Value)));
    }

    public async Task<ProjectRole?> AccessAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.FindAsync([projectId], ct) is { } project
            ? await LevelAsync(db, project, me, await directory.MembershipsAsync(me.Id, ct), ct)
            : null;

    public async Task<IReadOnlyList<Guid>?> ReadableAsync(CancellationToken ct) =>
        me.IsInstanceAdmin ? null : await Readable(db, me, await directory.MembershipsAsync(me.Id, ct)).Select(p => p.Id).ToListAsync(ct);

    public async Task<Guid> PersonalProjectAsync(CancellationToken ct)
    {
        var userId = me.Id;
        if (await db.Projects.FirstOrDefaultAsync(p => p.OwnerId == userId && p.IsPersonalDefault, ct) is { } existing)
            return existing.Id;
        var project = new Project
        {
            Id = Guid.CreateVersion7(), Name = "Personnel", OwnerId = userId, Visibility = ProjectVisibility.Private,
            IsPersonalDefault = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Projects.Add(project);
        try
        {
            await db.SaveChangesAsync(ct);
            return project.Id;
        }
        catch (DbUpdateException)
        {
            // Deux premières missions simultanées : l'index unique a laissé passer l'autre ; on reprend la sienne.
            db.ChangeTracker.Clear();
            return (await db.Projects.FirstAsync(p => p.OwnerId == userId && p.IsPersonalDefault, ct)).Id;
        }
    }
}

internal sealed class ProjectDirectory(ProjectsDb db, IDirectory directory) : IProjectDirectory
{
    public async Task<IReadOnlyList<string>> ReadersAsync(Guid projectId, CancellationToken ct)
    {
        var readers = new HashSet<string>(await directory.InstanceAdminsAsync(ct));
        if (await db.Projects.FindAsync([projectId], ct) is not { } project)
            return [.. readers];
        readers.Add(project.OwnerId);
        if (project.Visibility != ProjectVisibility.Private)
            readers.UnionWith(await db.Members.Where(m => m.ProjectId == projectId).Select(m => m.UserId).ToListAsync(ct));
        if (project.OrganizationId is { } org)
            readers.UnionWith(project.Visibility == ProjectVisibility.Organization
                ? await directory.MembersAsync(org, ct)
                : await directory.AdminsOfAsync(org, ct));
        return [.. readers];
    }

    public async Task<ProjectWorkspace> WorkspaceAsync(Guid projectId, CancellationToken ct)
    {
        if (await db.Projects.FindAsync([projectId], ct) is not { } project)
            return new ProjectWorkspace(false, null);
        if (project.Workspace is not null)
            return new ProjectWorkspace(true, project.Workspace);
        return new ProjectWorkspace((await directory.InstanceAdminsAsync(ct)).Contains(project.OwnerId), null);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> projectIds, CancellationToken ct)
    {
        var ids = projectIds.Distinct().ToList();
        return await db.Projects.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
    }
}

internal sealed class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string OwnerId { get; set; }
    public ProjectVisibility Visibility { get; set; }
    public bool IsPersonalDefault { get; set; }
    public string? Workspace { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ProjectMember
{
    public Guid ProjectId { get; set; }
    public required string UserId { get; set; }
    public ProjectRole Role { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public required string AddedBy { get; set; }
}

internal sealed class ProjectsDb(DbContextOptions<ProjectsDb> options) : DbContext(options)
{
    public const string Schema = "projects";

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> Members => Set<ProjectMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Project>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(80);
            e.Property(p => p.OwnerId).HasMaxLength(64);
            e.Property(p => p.Workspace).HasMaxLength(500);
            e.Property(p => p.Visibility).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(p => p.OrganizationId);
            // Deux index sur la même colonne : ils doivent avoir chacun un nom, sinon EF les fusionne.
            e.HasIndex(p => p.OwnerId, "IX_Projects_OwnerId");
            // Un seul projet personnel par utilisateur.
            e.HasIndex(p => p.OwnerId, "IX_Projects_PersonalDefault").IsUnique().HasFilter("\"IsPersonalDefault\"");
        });
        modelBuilder.Entity<ProjectMember>(e =>
        {
            e.ToTable("Members");
            e.HasKey(m => new { m.ProjectId, m.UserId });
            e.Property(m => m.UserId).HasMaxLength(64);
            e.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.AddedBy).HasMaxLength(64);
            e.HasIndex(m => m.UserId);
        });
    }
}
