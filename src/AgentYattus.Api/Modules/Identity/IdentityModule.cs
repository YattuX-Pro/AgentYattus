using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Identity;

/// <summary>L'utilisateur de la requête en cours (ADR 0009). Keycloak dit qui il est ; l'application, ce qu'il peut.</summary>
public interface ICurrentUser
{
    /// <summary>L'identifiant stable donné par Keycloak (« sub »).</summary>
    string Id { get; }
    string Username { get; }

    /// <summary>Administrateur de l'instance (rôle Keycloak llm-admin) : lit tout, gère les organisations.</summary>
    bool IsInstanceAdmin { get; }
}

/// <summary>Ce que les autres modules (Projects, Missions) ont besoin de savoir sur les personnes et les organisations.</summary>
public interface IDirectory
{
    Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken);
    Task<OrganizationRole?> RoleOfAsync(Guid organizationId, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, OrganizationRole>> MembershipsAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> MembersAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> InstanceAdminsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, UserRefDto>> ResolveAsync(IEnumerable<string> userIds, CancellationToken cancellationToken);
    Task<ProjectVisibility?> DefaultVisibilityAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, string>> OrganizationNamesAsync(IEnumerable<Guid> organizationIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> AdminsOfAsync(Guid organizationId, CancellationToken cancellationToken);
}

public static partial class IdentityModule
{
    private const int LookupMax = 10;

    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IDirectory, UserDirectory>();
        services.AddSingleton<UserDirectoryThrottle>();
        // SignalR identifie un utilisateur par « sub » : le hub peut écrire à une personne précise.
        services.AddSingleton<IUserIdProvider, SubUserIdProvider>();
        return services.AddModuleDb<IdentityDb>(IdentityDb.Schema);
    }

    /// <summary>Inscrit (ou met à jour) dans l'annuaire chaque utilisateur authentifié. À placer après UseAuthentication.</summary>
    public static IApplicationBuilder UseUserDirectory(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true && CurrentUser.IdOf(context.User) is { } id)
                await context.RequestServices.GetRequiredService<UserDirectoryThrottle>()
                    .TouchAsync(id, context.User, context.RequestServices, context.RequestAborted);
            await next();
        });

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(IdentityProtocol.Me, async (ICurrentUser me, IdentityDb db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([me.Id], ct);
            return TypedResults.Ok(new MeDto(me.Id, me.Username, user?.DisplayName, me.IsInstanceAdmin, await MembershipsOf(db, me.Id, ct)));
        });

        app.MapGet(IdentityProtocol.Users, async (IdentityDb db, CancellationToken ct) =>
        {
            var users = await db.Users.OrderBy(u => u.Username).ToListAsync(ct);
            var memberships = await (from m in db.Memberships join o in db.Organizations on m.OrganizationId equals o.Id
                                     select new { m.UserId, Dto = new MembershipDto(o.Id, o.Name, o.Slug, m.Role) }).ToListAsync(ct);
            return TypedResults.Ok(users.Select(u => new UserDto(
                u.Id, u.Username, u.DisplayName, u.IsInstanceAdmin, u.FirstSeenAt, u.LastSeenAt,
                [.. memberships.Where(m => m.UserId == u.Id).Select(m => m.Dto)])).ToArray());
        }).RequireAuthorization(Authentication.LlmAdminPolicy);

        // Pour partager : chercher quelqu'un. Pour ne pas laisser parcourir tout l'annuaire (« aa », « ab »…),
        // la recherche partielle ne porte que sur les personnes qui partagent une organisation avec l'appelant ;
        // les autres ne répondent qu'à leur nom d'utilisateur exact. Un administrateur de l'instance cherche partout.
        // ponytail: pas de limite de débit ; en ajouter une (AddRateLimiter) si l'instance s'ouvre au-delà d'une équipe.
        app.MapGet(IdentityProtocol.Lookup, async Task<Results<Ok<UserRefDto[]>, ValidationProblem>> (
            ICurrentUser me, IdentityDb db, CancellationToken ct, string q = "") =>
        {
            q = q.Trim();
            if (q.Length is < 2 or > 64)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Entre 2 et 64 caractères."] });
            var needle = q.ToLowerInvariant();
            var myOrgs = db.Memberships.Where(m => m.UserId == me.Id).Select(m => m.OrganizationId);
            var peers = db.Memberships.Where(m => myOrgs.Contains(m.OrganizationId)).Select(m => m.UserId);
            var instanceAdmin = me.IsInstanceAdmin;
            var found = await db.Users
                .Where(u => u.Id != me.Id)
                .Where(u => u.Username.ToLower() == needle
                    || ((instanceAdmin || peers.Contains(u.Id))
                        && (u.Username.ToLower().Contains(needle) || (u.DisplayName != null && u.DisplayName.ToLower().Contains(needle)))))
                .OrderBy(u => u.Username).Take(LookupMax)
                .Select(u => new UserRefDto(u.Id, u.Username, u.DisplayName)).ToArrayAsync(ct);
            return TypedResults.Ok(found);
        });

        var orgs = app.MapGroup(IdentityProtocol.Organizations);
        orgs.MapGet("/", async (ICurrentUser me, IdentityDb db, CancellationToken ct) =>
        {
            var mine = await db.Memberships.Where(m => m.UserId == me.Id).ToDictionaryAsync(m => m.OrganizationId, m => m.Role, ct);
            var ids = mine.Keys.ToList();
            var list = await db.Organizations.Where(o => me.IsInstanceAdmin || ids.Contains(o.Id)).OrderBy(o => o.Name).ToListAsync(ct);
            var result = new List<OrganizationDto>();
            foreach (var o in list)
                result.Add(await ToDtoAsync(db, o, mine.TryGetValue(o.Id, out var role) ? role : null, ct));
            return TypedResults.Ok(result);
        });

        orgs.MapPost("/", async Task<Results<Created<OrganizationDto>, ValidationProblem, Conflict>> (
            CreateOrganizationRequest request, ICurrentUser me, IdentityDb db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim() ?? "";
            var slug = request.Slug?.Trim() ?? "";
            if (name.Length is < 2 or > 80)
                return Invalid("name", "Le nom fait entre 2 et 80 caractères.");
            if (!SlugPattern().IsMatch(slug))
                return Invalid("slug", "L'identifiant : minuscules, chiffres et tirets, 2 à 40 caractères.");
            if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
                return TypedResults.Conflict();

            var org = new Organization { Id = Guid.CreateVersion7(), Name = name, Slug = slug, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = me.Id };
            db.Organizations.Add(org);
            // Le créateur devient le premier administrateur : une organisation n'est jamais sans administrateur.
            db.Memberships.Add(new Membership { OrganizationId = org.Id, UserId = me.Id, Role = OrganizationRole.Admin, AddedAt = org.CreatedAt, AddedBy = me.Id });
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"{IdentityProtocol.Organizations}/{org.Id}", await ToDtoAsync(db, org, OrganizationRole.Admin, ct));
        }).RequireAuthorization(Authentication.LlmAdminPolicy);

        orgs.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid id, UpdateOrganizationRequest request, ICurrentUser me, IdentityDb db, CancellationToken ct) =>
        {
            if (await ManageableAsync(db, me, id, ct) is not { } org)
                return await Hidden(db, me, id, ct);
            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80)
                return Invalid("name", "Le nom fait entre 2 et 80 caractères.");
            org.Name = name;
            org.DefaultVisibility = request.DefaultVisibility;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        orgs.MapPut("/{id:guid}/members/{userId}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid id, string userId, SetMemberRequest request, ICurrentUser me, IdentityDb db, CancellationToken ct) =>
        {
            if (await ManageableAsync(db, me, id, ct) is null)
                return await Hidden(db, me, id, ct);
            if (!Enum.IsDefined(request.Role))
                return Invalid("role", "Rôle inconnu.");
            if (await db.Users.FindAsync([userId], ct) is null)
                return TypedResults.NotFound();

            var membership = await db.Memberships.FindAsync([id, userId], ct);
            if (membership is { Role: OrganizationRole.Admin } && request.Role != OrganizationRole.Admin && await IsLastAdminAsync(db, id, ct))
                return Invalid("role", "C'est le dernier administrateur : nommez-en un autre d'abord.");
            if (membership is null)
                db.Memberships.Add(new Membership { OrganizationId = id, UserId = userId, Role = request.Role, AddedAt = DateTimeOffset.UtcNow, AddedBy = me.Id });
            else
                membership.Role = request.Role;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        orgs.MapDelete("/{id:guid}/members/{userId}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid id, string userId, ICurrentUser me, IdentityDb db, CancellationToken ct) =>
        {
            if (await ManageableAsync(db, me, id, ct) is null)
                return await Hidden(db, me, id, ct);
            if (await db.Memberships.FindAsync([id, userId], ct) is not { } membership)
                return TypedResults.NotFound();
            if (membership.Role == OrganizationRole.Admin && await IsLastAdminAsync(db, id, ct))
                return Invalid("userId", "C'est le dernier administrateur : nommez-en un autre d'abord.");
            db.Memberships.Remove(membership);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        return app;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])$")]
    private static partial Regex SlugPattern();

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<Organization?> ManageableAsync(IdentityDb db, ICurrentUser me, Guid id, CancellationToken ct)
    {
        var org = await db.Organizations.FindAsync([id], ct);
        if (org is null)
            return null;
        if (me.IsInstanceAdmin)
            return org;
        return await db.Memberships.AnyAsync(m => m.OrganizationId == id && m.UserId == me.Id && m.Role == OrganizationRole.Admin, ct) ? org : null;
    }

    /// <summary>Un membre non administrateur reçoit 403 ; quelqu'un d'extérieur, 404 (l'organisation ne lui est pas révélée).</summary>
    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> Hidden(IdentityDb db, ICurrentUser me, Guid id, CancellationToken ct) =>
        await db.Memberships.AnyAsync(m => m.OrganizationId == id && m.UserId == me.Id, ct) ? TypedResults.Forbid() : TypedResults.NotFound();

    private static async Task<bool> IsLastAdminAsync(IdentityDb db, Guid id, CancellationToken ct) =>
        await db.Memberships.CountAsync(m => m.OrganizationId == id && m.Role == OrganizationRole.Admin, ct) <= 1;

    private static async Task<IReadOnlyList<MembershipDto>> MembershipsOf(IdentityDb db, string userId, CancellationToken ct) =>
        await (from m in db.Memberships where m.UserId == userId
               join o in db.Organizations on m.OrganizationId equals o.Id
               orderby o.Name
               select new MembershipDto(o.Id, o.Name, o.Slug, m.Role)).ToListAsync(ct);

    private static async Task<OrganizationDto> ToDtoAsync(IdentityDb db, Organization org, OrganizationRole? myRole, CancellationToken ct)
    {
        var members = await (from m in db.Memberships where m.OrganizationId == org.Id
                             join u in db.Users on m.UserId equals u.Id
                             orderby u.Username
                             select new MemberDto(u.Id, u.Username, u.DisplayName, m.Role)).ToListAsync(ct);
        return new OrganizationDto(org.Id, org.Name, org.Slug, org.DefaultVisibility, members, myRole);
    }
}

internal sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    public string Id => IdOf(User) ?? throw new InvalidOperationException("Aucun utilisateur authentifié.");
    public string Username => User.Identity?.Name ?? Id;
    public bool IsInstanceAdmin => User.IsInRole(Authentication.LlmAdminRole);

    public static string? IdOf(ClaimsPrincipal user) => user.FindFirst("sub")?.Value is { Length: > 0 and <= 64 } sub ? sub : null;
}

internal sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => CurrentUser.IdOf(connection.User);
}

/// <summary>
/// L'annuaire se met à jour à chaque requête, mais au plus une écriture toutes les 5 minutes par personne
/// (sauf si son nom ou son rôle d'administrateur change).
/// </summary>
internal sealed class UserDirectoryThrottle
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, string Username, bool Admin)> _seen = new();

    public async Task TouchAsync(string id, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct)
    {
        var username = (principal.Identity?.Name ?? id)[..Math.Min((principal.Identity?.Name ?? id).Length, 100)];
        var displayName = principal.FindFirst("name")?.Value is { Length: > 0 } n ? n[..Math.Min(n.Length, 120)] : null;
        var admin = principal.IsInRole(Authentication.LlmAdminRole);
        var now = DateTimeOffset.UtcNow;
        if (_seen.TryGetValue(id, out var last) && now - last.At < Interval && last.Username == username && last.Admin == admin)
            return;

        var db = services.GetRequiredService<IdentityDb>();
        var user = await db.Users.FindAsync([id], ct);
        if (user is null)
            db.Users.Add(new User { Id = id, Username = username, DisplayName = displayName, IsInstanceAdmin = admin, FirstSeenAt = now, LastSeenAt = now });
        else
        {
            user.Username = username;
            user.DisplayName = displayName;
            user.IsInstanceAdmin = admin;
            user.LastSeenAt = now;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (user is null)
        {
            // Deux premières requêtes simultanées : l'autre a déjà inscrit l'utilisateur.
            db.ChangeTracker.Clear();
        }
        _seen[id] = (now, username, admin);
    }
}

internal sealed class UserDirectory(IdentityDb db) : IDirectory
{
    public Task<bool> ExistsAsync(string userId, CancellationToken ct) => db.Users.AnyAsync(u => u.Id == userId, ct);

    public async Task<OrganizationRole?> RoleOfAsync(Guid organizationId, string userId, CancellationToken ct) =>
        (await db.Memberships.FindAsync([organizationId, userId], ct))?.Role;

    public async Task<IReadOnlyDictionary<Guid, OrganizationRole>> MembershipsAsync(string userId, CancellationToken ct) =>
        await db.Memberships.Where(m => m.UserId == userId).ToDictionaryAsync(m => m.OrganizationId, m => m.Role, ct);

    public async Task<IReadOnlyList<string>> MembersAsync(Guid organizationId, CancellationToken ct) =>
        await db.Memberships.Where(m => m.OrganizationId == organizationId).Select(m => m.UserId).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> InstanceAdminsAsync(CancellationToken ct) =>
        await db.Users.Where(u => u.IsInstanceAdmin).Select(u => u.Id).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<string, UserRefDto>> ResolveAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new UserRefDto(u.Id, u.Username, u.DisplayName), ct);
    }

    public async Task<ProjectVisibility?> DefaultVisibilityAsync(Guid organizationId, CancellationToken ct) =>
        (await db.Organizations.FindAsync([organizationId], ct))?.DefaultVisibility;

    public async Task<IReadOnlyDictionary<Guid, string>> OrganizationNamesAsync(IEnumerable<Guid> organizationIds, CancellationToken ct)
    {
        var ids = organizationIds.Distinct().ToList();
        return await db.Organizations.Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, ct);
    }

    public async Task<IReadOnlyList<string>> AdminsOfAsync(Guid organizationId, CancellationToken ct) =>
        await db.Memberships.Where(m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Admin).Select(m => m.UserId).ToListAsync(ct);
}

internal sealed class User
{
    public required string Id { get; set; }
    public required string Username { get; set; }
    public string? DisplayName { get; set; }
    public bool IsInstanceAdmin { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

internal sealed class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public ProjectVisibility DefaultVisibility { get; set; } = ProjectVisibility.Private;
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

internal sealed class Membership
{
    public Guid OrganizationId { get; set; }
    public required string UserId { get; set; }
    public OrganizationRole Role { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public required string AddedBy { get; set; }
}

internal sealed class IdentityDb(DbContextOptions<IdentityDb> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Id).HasMaxLength(64);
            e.Property(u => u.Username).HasMaxLength(100);
            e.Property(u => u.DisplayName).HasMaxLength(120);
        });
        modelBuilder.Entity<Organization>(e =>
        {
            e.Property(o => o.Name).HasMaxLength(80);
            e.Property(o => o.Slug).HasMaxLength(40);
            e.HasIndex(o => o.Slug).IsUnique();
            e.Property(o => o.DefaultVisibility).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.CreatedBy).HasMaxLength(64);
        });
        modelBuilder.Entity<Membership>(e =>
        {
            e.HasKey(m => new { m.OrganizationId, m.UserId });
            e.Property(m => m.UserId).HasMaxLength(64);
            e.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.AddedBy).HasMaxLength(64);
            e.HasIndex(m => m.UserId);
        });
    }
}
