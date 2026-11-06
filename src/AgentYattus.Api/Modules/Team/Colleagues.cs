using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Identity;
using AgentYattus.Api.Modules.Projects;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Team;

/// <summary>Ce que les autres modules (Missions) ont besoin de savoir sur l'équipe d'un projet.</summary>
public interface ITeamDirectory
{
    /// <summary>
    /// Par rôle, la présentation du collègue à ajouter à la consigne : son prénom et ses traits.
    /// Un rôle sans collègue n'a pas d'entrée.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> StylesAsync(Guid projectId, CancellationToken cancellationToken);
}

internal sealed class TeamDirectory(TeamDb db) : ITeamDirectory
{
    public async Task<IReadOnlyDictionary<string, string>> StylesAsync(Guid projectId, CancellationToken ct)
    {
        var colleagues = await db.Colleagues.AsNoTracking().Where(c => c.ProjectId == projectId && c.Active).ToListAsync(ct);
        if (colleagues.Count == 0)
            return new Dictionary<string, string>();
        var traits = (await TeamModule.TraitsAsync(db, ct)).ToDictionary(t => t.Key, t => t.Style);
        return colleagues.ToDictionary(c => c.Role, c => TeamModule.Style(c.Name, c.TraitKeys.Select(k => traits.GetValueOrDefault(k)).OfType<string>()));
    }
}

public static partial class TeamModule
{
    /// <summary>La présentation ajoutée à la consigne : « Tu t'appelles Inès. » puis la phrase de chaque trait.</summary>
    internal static string Style(string name, IEnumerable<string> traitStyles) =>
        string.Join(' ', new[] { $"Tu t'appelles {name}." }.Concat(traitStyles));

    private static void MapColleagues(IEndpointRouteBuilder app)
    {
        // L'équipe d'un projet, telle que le bureau la joue. La première lecture installe l'équipe de départ.
        app.MapGet("/projects/{projectId:guid}/team", async Task<Results<Ok<ProjectTeamDto>, NotFound>> (
            Guid projectId, TeamDb db, IProjectAccess access, IDirectory directory, CancellationToken ct) =>
        {
            if (await access.AccessAsync(projectId, ct) is not { } role)
                return TypedResults.NotFound();
            await EnsureDefaultTeamAsync(db, projectId, ct);
            var settings = await SettingsAsync(db, ct);
            var colleagues = await db.Colleagues.AsNoTracking().Where(c => c.ProjectId == projectId && c.Active).ToListAsync(ct);
            return TypedResults.Ok(new ProjectTeamDto(
                projectId, await ToDtosAsync(colleagues, directory, ct), settings.LeaderName, settings.LeaderPortraitId,
                settings.ManagerPortraitId, settings.TypingMs, settings.PauseMs, await LinesAsync(db, ct),
                CanRecruit: role is ProjectRole.Owner or ProjectRole.Editor));
        });

        // Recruter : le droit de lancer des missions dans le projet. Le siège du rôle change de titulaire.
        app.MapPost("/projects/{projectId:guid}/colleagues", async Task<Results<Created<ColleagueDto>, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid projectId, RecruitRequest request, TeamDb db, IProjectAccess access, ICurrentUser me, IDirectory directory, CancellationToken ct) =>
        {
            switch (await access.AccessAsync(projectId, ct))
            {
                case null: return TypedResults.NotFound();
                case ProjectRole.Reader: return TypedResults.Forbid();
            }

            var name = request.Name.Trim();
            var traitKeys = request.Traits.Select(t => t.Trim()).Distinct().ToList();
            var knownTraits = (await TraitsAsync(db, ct)).Select(t => t.Key).ToHashSet();
            var errors = new Dictionary<string, string[]>();
            if (!AgentCatalog.RoleKeys.Contains(request.Role))
                errors["role"] = [$"Rôle inconnu. Rôles : {string.Join(", ", AgentCatalog.RoleKeys)}."];
            if (!ValidName(name))
                errors["name"] = ["De 1 à 24 caractères : lettres, espaces, apostrophes et tirets."];
            if (traitKeys.Count > 5 || traitKeys.Any(k => !knownTraits.Contains(k)))
                errors["traits"] = ["Cinq traits au plus, parmi ceux proposés."];
            if (!await db.Portraits.AnyAsync(p => p.Id == request.PortraitId && p.Active, ct))
                errors["portraitId"] = ["Ce portrait n'existe pas ou n'est plus proposé."];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            await EnsureDefaultTeamAsync(db, projectId, ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var previous in await db.Colleagues.Where(c => c.ProjectId == projectId && c.Role == request.Role && c.Active).ToListAsync(ct))
            {
                previous.Active = false;
                previous.RemovedAt = now;
                previous.RemovedBy = me.Username;
            }

            // Deux temps : l'ancien siège est libéré avant d'installer le nouveau, sinon l'index unique refuse le second.
            await db.SaveChangesAsync(ct);
            var colleague = new Colleague
            {
                Id = Guid.CreateVersion7(), ProjectId = projectId, Role = request.Role, PortraitId = request.PortraitId, Name = name,
                Traits = string.Join(',', traitKeys), CreatedAt = now, CreatedBy = me.Id,
            };
            db.Colleagues.Add(colleague);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created(TeamProtocol.Colleague(projectId, colleague.Id), (await ToDtosAsync([colleague], directory, ct))[0]);
        });

        app.MapDelete("/projects/{projectId:guid}/colleagues/{colleagueId:guid}", async Task<Results<NoContent, NotFound, ForbidHttpResult>> (
            Guid projectId, Guid colleagueId, TeamDb db, IProjectAccess access, ICurrentUser me, CancellationToken ct) =>
        {
            switch (await access.AccessAsync(projectId, ct))
            {
                case null: return TypedResults.NotFound();
                case ProjectRole.Reader: return TypedResults.Forbid();
            }

            var colleague = await db.Colleagues.FirstOrDefaultAsync(c => c.Id == colleagueId && c.ProjectId == projectId && c.Active, ct);
            if (colleague is null)
                return TypedResults.NotFound();
            colleague.Active = false;
            colleague.RemovedAt = DateTimeOffset.UtcNow;
            colleague.RemovedBy = me.Username;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        // L'historique des équipes, pour la page Équipes de l'admin : les projets que l'appelant peut lire.
        app.MapGet(TeamProtocol.Colleagues, async (TeamDb db, IProjectAccess access, IProjectDirectory projects, IDirectory directory, CancellationToken ct) =>
        {
            var readable = await access.ReadableAsync(ct);
            var query = db.Colleagues.AsNoTracking();
            if (readable is not null)
                query = query.Where(c => readable.Contains(c.ProjectId));
            // Les identifiants sont des UUID v7 : leur ordre est celui de la création (et SQLite ne trie pas les dates avec fuseau).
            var rows = await query.OrderByDescending(c => c.Id).Take(500).ToListAsync(ct);
            var names = await projects.NamesAsync(rows.Select(r => r.ProjectId).Distinct(), ct);
            var dtos = await ToDtosAsync(rows, directory, ct);
            return TypedResults.Ok(dtos.Select(d => new ColleagueEntryDto(d, names.GetValueOrDefault(d.ProjectId, "?"))).ToList());
        });
    }

    /// <summary>
    /// Un projet qui n'a jamais eu d'équipe reçoit l'équipe de départ (réglée dans l'admin). Un projet dont on a
    /// retiré tous les collègues n'en reçoit pas une nouvelle : il a déjà eu une équipe.
    /// </summary>
    private static async Task EnsureDefaultTeamAsync(TeamDb db, Guid projectId, CancellationToken ct)
    {
        if (await db.Colleagues.AnyAsync(c => c.ProjectId == projectId, ct))
            return;
        var seats = (await SettingsAsync(db, ct)).DefaultTeam;
        var active = await db.Portraits.Where(p => p.Active).Select(p => p.Id).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var seat in seats.Where(s => active.Contains(s.PortraitId) && AgentCatalog.RoleKeys.Contains(s.Role)))
            db.Colleagues.Add(new Colleague
            {
                Id = Guid.CreateVersion7(), ProjectId = projectId, Role = seat.Role, PortraitId = seat.PortraitId, Name = seat.Name,
                CreatedAt = now, CreatedBy = "équipe de départ",
            });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Deux premières lectures simultanées : l'autre a installé l'équipe, l'index unique a refusé la nôtre.
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<List<ColleagueDto>> ToDtosAsync(IReadOnlyList<Colleague> colleagues, IDirectory directory, CancellationToken ct)
    {
        var people = await directory.ResolveAsync(colleagues.Select(c => c.CreatedBy).Distinct(), ct);
        return colleagues.Select(c => new ColleagueDto(
            c.Id, c.ProjectId, c.Role, c.PortraitId, c.Name, c.TraitKeys, people.GetValueOrDefault(c.CreatedBy), c.CreatedAt,
            c.Active, c.RemovedAt)).ToList();
    }
}
