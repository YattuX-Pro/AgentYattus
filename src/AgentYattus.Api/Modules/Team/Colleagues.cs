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
    /// Par rôle, le collègue qui l'occupe : sa présentation (prénom, traits) à ajouter à la consigne, et ses compétences.
    /// Un rôle sans collègue n'a pas d'entrée.
    /// </summary>
    Task<IReadOnlyDictionary<string, AgentSeat>> SeatsAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Range une compétence apprise sur le collègue du rôle (dix au plus) ; sans collègue, elle reste dans la bibliothèque.</summary>
    Task AttachSkillAsync(Guid projectId, string role, string skill, CancellationToken cancellationToken);
}

internal sealed class TeamDirectory(TeamDb db) : ITeamDirectory
{
    public async Task<IReadOnlyDictionary<string, AgentSeat>> SeatsAsync(Guid projectId, CancellationToken ct)
    {
        var colleagues = await db.Colleagues.AsNoTracking().Where(c => c.ProjectId == projectId && c.Active).ToListAsync(ct);
        var traits = (await TeamModule.TraitsAsync(db, ct)).ToDictionary(t => t.Key, t => t.Style);
        var portraits = await db.Portraits.AsNoTracking().Select(p => new { p.Id, p.Slug, p.MouthX, p.MouthY }).ToListAsync(ct);
        var seats = colleagues.ToDictionary(c => c.Role, c => new AgentSeat(
            TeamModule.Style(c.Name, c.TraitKeys.Select(k => traits.GetValueOrDefault(k)).OfType<string>()), c.SkillKeys, c.Leader, c.Name, c.Id, c.PortraitId,
            portraits.FirstOrDefault(p => p.Id == c.PortraitId)?.MouthX ?? 0.56,
            portraits.FirstOrDefault(p => p.Id == c.PortraitId)?.MouthY ?? 0.37));
        var rh = portraits.FirstOrDefault(p => p.Slug == "nora");
        seats["_rh"] = new AgentSeat("Service RH intégré", [], Name: "Agent RH", PortraitId: rh?.Id,
            MouthX: rh?.MouthX ?? 0.56, MouthY: rh?.MouthY ?? 0.37);
        return seats;
    }

    public async Task AttachSkillAsync(Guid projectId, string role, string skill, CancellationToken ct)
    {
        var colleague = await db.Colleagues.FirstOrDefaultAsync(c => c.ProjectId == projectId && c.Role == role && c.Active, ct);
        if (colleague is null || colleague.SkillKeys.Contains(skill) || colleague.SkillKeys.Count >= 10)
            return;
        colleague.Skills = string.Join(',', [.. colleague.SkillKeys, skill]);
        await db.SaveChangesAsync(ct);
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
            Guid projectId, TeamDb db, IProjectAccess access, IDirectory directory, IAgentCatalog catalog, CancellationToken ct) =>
        {
            if (await access.AccessAsync(projectId, ct) is not { } role)
                return TypedResults.NotFound();
            await EnsureDefaultTeamAsync(db, projectId, await catalog.RoleKeysAsync(ct), ct);
            var settings = await SettingsAsync(db, ct);
            var colleagues = await db.Colleagues.AsNoTracking().Where(c => c.ProjectId == projectId && c.Active).ToListAsync(ct);
            return TypedResults.Ok(new ProjectTeamDto(
                projectId, await ToDtosAsync(colleagues, directory, ct), settings.ManagerPortraitId, settings.TypingMs, settings.PauseMs, await LinesAsync(db, ct),
                CanRecruit: role is ProjectRole.Owner or ProjectRole.Editor));
        });

        // Recruter : le droit de lancer des missions dans le projet. Le siège du rôle change de titulaire.
        // Recruté comme chef d'équipe (leçon 129), il reprend le titre : l'ancien chef reste, simple collègue.
        app.MapPost("/projects/{projectId:guid}/colleagues", async Task<Results<Created<ColleagueDto>, NotFound, ForbidHttpResult, ValidationProblem>> (
            Guid projectId, RecruitRequest request, TeamDb db, IProjectAccess access, ICurrentUser me, IDirectory directory, IAgentCatalog catalog, IRecruitmentCompletion completion,
            CancellationToken ct) =>
        {
            switch (await access.AccessAsync(projectId, ct))
            {
                case null: return TypedResults.NotFound();
                case ProjectRole.Reader: return TypedResults.Forbid();
            }

            if (request.RecruitmentSource is { } source)
            {
                if (await db.Colleagues.FirstOrDefaultAsync(c => c.RecruitmentSource == source && c.ProjectId == projectId && c.CreatedBy == me.Id, ct) is { } existing)
                {
                    await completion.CompleteAsync(source, request.RecruitmentMission ?? source, projectId, me.Id, existing.Id, existing.Name, ct);
                    return TypedResults.Created(TeamProtocol.Colleague(projectId, existing.Id), (await ToDtosAsync([existing], directory, ct)).Single());
                }
                if (!await completion.CanCompleteAsync(source, request.RecruitmentMission ?? source, projectId, me.Id, ct)) return TypedResults.NotFound();
            }
            var name = (request.Name ?? "").Trim();
            var traitKeys = (request.Traits ?? []).Select(t => (t ?? "").Trim()).Distinct().ToList();
            var skillKeys = (request.Skills ?? []).Select(s => (s ?? "").Trim()).Distinct().ToList();
            var knownTraits = (await TraitsAsync(db, ct)).Select(t => t.Key).ToHashSet();
            var errors = new Dictionary<string, string[]>();
            var roles = await catalog.RoleKeysAsync(ct);
            if (request.Role is null || !roles.Contains(request.Role))
                errors["role"] = [$"Rôle inconnu. Rôles : {string.Join(", ", roles)}."];
            if (!ValidName(name))
                errors["name"] = ["De 1 à 24 caractères : lettres, espaces, apostrophes et tirets."];
            if (traitKeys.Count > 5 || traitKeys.Any(k => !knownTraits.Contains(k)))
                errors["traits"] = ["Cinq traits au plus, parmi ceux proposés."];
            // Seulement des compétences que l'on voit : celles de l'instance et les siennes (leçon 104).
            if (skillKeys.Count > 0)
            {
                var visible = await catalog.SkillKeysAsync(ct);
                if (skillKeys.Count > 10 || skillKeys.Any(k => !visible.Contains(k)))
                    errors["skills"] = ["Dix compétences au plus, parmi les tiennes et celles de l'instance."];
            }
            if (!await db.Portraits.AnyAsync(p => p.Id == request.PortraitId && p.Active, ct))
                errors["portraitId"] = ["Ce portrait n'existe pas ou n'est plus proposé."];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await EnsureDefaultTeamAsync(db, projectId, roles, ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var previous in await db.Colleagues.Where(c => c.ProjectId == projectId && c.Role == request.Role && c.Active).ToListAsync(ct))
            {
                previous.Active = false;
                previous.RemovedAt = now;
                previous.RemovedBy = me.Username;
            }
            if (request.Leader)
                foreach (var leader in await db.Colleagues.Where(c => c.ProjectId == projectId && c.Active && c.Leader).ToListAsync(ct))
                    leader.Leader = false;

            // Deux temps : l'ancien siège (et l'ancien chef) libérés avant d'installer le nouveau, sinon les index uniques refusent le second.
            await db.SaveChangesAsync(ct);
            var colleague = new Colleague
            {
                Id = Guid.CreateVersion7(), ProjectId = projectId, Role = request.Role!, RecruitmentSource = request.RecruitmentSource, PortraitId = request.PortraitId, Name = name,
                Traits = string.Join(',', traitKeys), Skills = string.Join(',', skillKeys), Leader = request.Leader, CreatedAt = now, CreatedBy = me.Id,
            };
            db.Colleagues.Add(colleague);
            try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            catch (DbUpdateException) when (request.RecruitmentSource is not null)
            {
                await transaction.RollbackAsync(ct); db.ChangeTracker.Clear();
                var prior = await db.Colleagues.FirstOrDefaultAsync(c => c.RecruitmentSource == request.RecruitmentSource, ct);
                if (prior is null) throw;
                await completion.CompleteAsync(request.RecruitmentSource.Value, request.RecruitmentMission ?? request.RecruitmentSource.Value, projectId, me.Id, prior.Id, prior.Name, ct);
                return TypedResults.Created(TeamProtocol.Colleague(projectId, prior.Id), (await ToDtosAsync([prior], directory, ct)).Single());
            }
            if (request.RecruitmentSource is { } completedSource)
                await completion.CompleteAsync(completedSource, request.RecruitmentMission ?? completedSource, projectId, me.Id, colleague.Id, colleague.Name, ct);
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
    private static async Task EnsureDefaultTeamAsync(TeamDb db, Guid projectId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (await db.Colleagues.AnyAsync(c => c.ProjectId == projectId, ct))
            return;
        var seats = (await SettingsAsync(db, ct)).DefaultTeam;
        var active = await db.Portraits.Where(p => p.Active).Select(p => p.Id).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var seat in seats.Where(s => active.Contains(s.PortraitId) && roles.Contains(s.Role)))
            db.Colleagues.Add(new Colleague
            {
                Id = Guid.CreateVersion7(), ProjectId = projectId, Role = seat.Role, PortraitId = seat.PortraitId, Name = seat.Name,
                Leader = seat.Leader, CreatedAt = now, CreatedBy = "équipe de départ",
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
            c.Active, c.RemovedAt, c.SkillKeys, c.Leader, c.RecruitmentSource)).ToList();
    }
}
