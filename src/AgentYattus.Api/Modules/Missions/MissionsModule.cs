using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Identity;
using AgentYattus.Api.Modules.Settings;
using AgentYattus.Api.Modules.Projects;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Missions;

public static class MissionsModule
{
    private const int MaxGoalLength = 4000;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static IServiceCollection AddMissionsModule(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<MissionQueue>();
        services.AddSingleton<MissionCancellations>();
        services.AddScoped<MissionRunner>();
        services.AddHostedService<MissionWorker>();
        return services.AddModuleDb<MissionsDb>(MissionsDb.Schema);
    }

    public static IEndpointRouteBuilder MapMissionsEndpoints(this IEndpointRouteBuilder app)
    {
        var missions = app.MapGroup("/missions");
        missions.MapGet("/", ListAsync);
        missions.MapPost("/", CreateAsync);
        missions.MapGet("/{id:guid}", GetAsync);
        missions.MapPost("/{id:guid}/decision", DecideAsync);
        missions.MapPost("/{id:guid}/messages", ReplyAsync);
        missions.MapGet("/{id:guid}/turns", GetTurnsAsync);
        missions.MapPost("/{id:guid}/cancel", CancelAsync);
        missions.MapPost("/{id:guid}/retry", RetryAsync);
        missions.MapGet("/{id:guid}/events", GetEventsAsync);
        app.MapHub<MissionsHub>(MissionsHubProtocol.Path);
        app.MapGet(AgentsProtocol.Performance, async (MissionsDb db, CancellationToken ct) => TypedResults.Ok(await PerformanceAsync(db, ct)))
            .RequireAuthorization(Authentication.LlmAdminPolicy);
        // Pour tous : les compétences que l'on voit, et ses propres missions (toutes pour l'administrateur).
        app.MapGet(SkillsProtocol.Performance, async (MissionsDb db, AuditLog audit, IAgentCatalog agents, ICurrentUser me, CancellationToken ct) =>
            TypedResults.Ok(await SkillPerformanceAsync(db, audit, await agents.SkillKeysAsync(ct), me.IsInstanceAdmin ? null : me.Id, ct)));
        app.MapGet(SettingsProtocol.Budget, async (MissionsDb db, IInstanceSettings instance, CancellationToken ct) =>
            TypedResults.Ok(await BudgetAsync(db, await instance.GetAsync(ct), ct)))
            .RequireAuthorization(Authentication.LlmAdminPolicy);
        return app;
    }

    private static async Task<Results<Accepted<MissionDto>, ValidationProblem, NotFound, ForbidHttpResult>> CreateAsync(
        CreateMissionRequest request, MissionsDb db, MissionQueue queue, ICurrentUser me, IProjectAccess projects,
        IProjectDirectory directory, IAgentCatalog agents, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Goal) || request.Goal.Length > MaxGoalLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["goal"] = [$"Le but est obligatoire et fait au plus {MaxGoalLength} caractères."],
            });
        // L'agent choisi (ADR 0013) : pour une Question seulement, et parmi ceux qui sont proposés.
        var agent = string.IsNullOrWhiteSpace(request.Agent) ? null : request.Agent.Trim();
        if (agent is not null && (request.Kind != MissionKind.Question || !(await agents.RoleKeysAsync(cancellationToken)).Contains(agent)))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["agent"] = ["Un agent se choisit pour une mission Question, parmi ceux proposés."],
            });

        // Sans projet précisé (le bureau d'aujourd'hui) : le projet personnel de l'auteur.
        var projectId = request.ProjectId ?? await projects.PersonalProjectAsync(cancellationToken);
        switch (await projects.AccessAsync(projectId, cancellationToken))
        {
            case null:
                return TypedResults.NotFound();
            case ProjectRole.Reader:
                return TypedResults.Forbid();
        }

        var mission = new Mission
        {
            Id = Guid.CreateVersion7(),
            Goal = request.Goal.Trim(),
            Status = MissionStatus.Pending,
            Kind = request.Kind,
            Agent = agent,
            CreatedAt = DateTimeOffset.UtcNow,
            ProjectId = projectId,
            CreatedBy = me.Id,
            CreatedByName = me.Username,
        };
        db.Missions.Add(mission);
        await db.SaveChangesAsync(cancellationToken);
        await queue.Writer.WriteAsync(mission.Id, cancellationToken);

        var names = await directory.NamesAsync([projectId], cancellationToken);
        return TypedResults.Accepted($"/missions/{mission.Id}", mission.ToDto(names.GetValueOrDefault(projectId)));
    }

    // ponytail: une seule page, les plus récentes d'abord ; ajouter un curseur (?before=) quand 100 ne suffira plus.
    private static async Task<Results<Ok<MissionDto[]>, ValidationProblem>> ListAsync(
        MissionsDb db, IProjectAccess projects, IProjectDirectory directory, CancellationToken cancellationToken,
        int limit = DefaultPageSize, Guid? projectId = null)
    {
        if (limit is < 1 or > MaxPageSize)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = [$"La limite est comprise entre 1 et {MaxPageSize}."],
            });

        // Seulement les missions des projets lisibles (tous pour un administrateur de l'instance).
        var query = db.Missions.AsQueryable();
        if (await projects.ReadableAsync(cancellationToken) is { } readable)
            query = query.Where(m => m.ProjectId != null && readable.Contains(m.ProjectId.Value));
        if (projectId is { } only)
            query = query.Where(m => m.ProjectId == only);

        // Les identifiants v7 commencent par l'heure de création : trier par Id, c'est trier par date.
        // (SQLite ne sait pas trier sur un DateTimeOffset.)
        var page = await query.OrderByDescending(m => m.Id).Take(limit).ToListAsync(cancellationToken);
        var names = await directory.NamesAsync(page.Where(m => m.ProjectId != null).Select(m => m.ProjectId!.Value), cancellationToken);
        return TypedResults.Ok(page.Select(m => m.ToDto(m.ProjectId is { } p ? names.GetValueOrDefault(p) : null)).ToArray());
    }

    /// <summary>Ce qui est dépensé ce mois civil (temps universel), en micro-dollars.</summary>
    // ponytail: filtre du mois en mémoire (SQLite ne compare pas les DateTimeOffset) ; une colonne « mois » si la table grossit.
    internal static async Task<long> SpentThisMonthMicrosAsync(MissionsDb db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var costs = await db.Missions.Where(m => m.CostMicros > 0).Select(m => new { m.CreatedAt, m.CostMicros }).ToListAsync(ct);
        return costs.Where(c => c.CreatedAt >= start).Sum(c => c.CostMicros);
    }

    /// <summary>
    /// Par agent, les missions terminées où il a travaillé : une question compte pour son agent (l'Analyste par défaut),
    /// une revue pour l'Analyste et pour le Rédacteur. Regroupé en SQL (type, agent, statut), puis réparti ici.
    /// </summary>
    internal static async Task<List<AgentPerformanceDto>> PerformanceAsync(MissionsDb db, CancellationToken ct)
    {
        MissionStatus[] finished = [MissionStatus.Completed, MissionStatus.Failed, MissionStatus.Cancelled];
        var groups = await db.Missions.AsNoTracking().Where(m => finished.Contains(m.Status))
            .GroupBy(m => new { m.Kind, m.Agent, m.Status })
            .Select(g => new { g.Key.Kind, g.Key.Agent, g.Key.Status, Count = g.Count(), Cost = g.Sum(m => m.CostMicros) })
            .ToListAsync(ct);
        var totals = new Dictionary<string, (int Finished, int Completed, long Cost)>();
        foreach (var g in groups)
        {
            IEnumerable<string> roles = g.Kind == MissionKind.Review ? AgentsModule.RolesFor(MissionKind.Review) : [g.Agent ?? AgentsModule.RolesFor(MissionKind.Question)[0]];
            foreach (var role in roles)
            {
                var (f, c, cost) = totals.GetValueOrDefault(role);
                totals[role] = (f + g.Count, c + (g.Status == MissionStatus.Completed ? g.Count : 0), cost + g.Cost);
            }
        }

        return [.. totals.Select(t => new AgentPerformanceDto(t.Key, t.Value.Finished, t.Value.Completed, t.Value.Cost / 1_000_000m / t.Value.Finished))];
    }

    /// <summary>
    /// Par compétence : les missions terminées où un agent l'a chargée (« competence:rôle:clé » dans le journal),
    /// et combien ont abouti. Une mission qui l'a chargée trois fois compte une fois.
    /// </summary>
    internal static async Task<List<SkillPerformanceDto>> SkillPerformanceAsync(
        MissionsDb db, AuditLog audit, IReadOnlySet<string> visible, string? onlyBy, CancellationToken ct)
    {
        var loads = (await audit.EventsStartingWithAsync("competence:", ct))
            .Select(e => (e.MissionId, Skill: e.Type.Split(':', 3) is [_, _, var key] ? key : ""))
            .Where(e => visible.Contains(e.Skill)).Distinct().ToList();
        var ids = loads.Select(l => l.MissionId).Distinct().ToList();
        MissionStatus[] finished = [MissionStatus.Completed, MissionStatus.Failed, MissionStatus.Cancelled];
        var statuses = await db.Missions.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && finished.Contains(m.Status) && (onlyBy == null || m.CreatedBy == onlyBy))
            .ToDictionaryAsync(m => m.Id, m => m.Status, ct);
        return [.. loads.Where(l => statuses.ContainsKey(l.MissionId)).GroupBy(l => l.Skill)
            .Select(g => new SkillPerformanceDto(g.Key, g.Count(), g.Count(l => statuses[l.MissionId] == MissionStatus.Completed)))];
    }

    internal static async Task<BudgetDto> BudgetAsync(MissionsDb db, InstanceSettings settings, CancellationToken ct)
    {
        var spent = await SpentThisMonthMicrosAsync(db, ct) / 1_000_000m;
        var budget = settings.MonthlyBudget;
        return new BudgetDto(budget, spent, settings.BudgetAlertPercent, settings.BlockAtBudget,
            Alert: budget is { } b && spent >= b * settings.BudgetAlertPercent / 100m,
            Reached: budget is { } c && spent >= c);
    }

    /// <summary>
    /// Ce que l'utilisateur peut faire sur une mission : rien (null, la mission « n'existe pas » pour lui), lire, ou éditer.
    /// Une mission sans projet (antérieure aux projets) n'est lisible que par un administrateur de l'instance.
    /// </summary>
    private static async Task<ProjectRole?> AccessAsync(Mission mission, ICurrentUser me, IProjectAccess projects, CancellationToken ct) =>
        mission.ProjectId is { } projectId
            ? await projects.AccessAsync(projectId, ct)
            : me.IsInstanceAdmin ? ProjectRole.Reader : null;

    private static async Task<Results<Accepted, NotFound, Conflict, ForbidHttpResult>> DecideAsync(
        Guid id, DecisionRequest request, MissionsDb db, AuditLog audit, MissionQueue queue, ICurrentUser me, IProjectAccess projects,
        CancellationToken cancellationToken)
    {
        if (await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken) is not { } mission)
            return TypedResults.NotFound();
        switch (await AccessAsync(mission, me, projects, cancellationToken))
        {
            case null:
                return TypedResults.NotFound();
            case ProjectRole.Reader:
                // Un lecteur (ou un administrateur de l'instance hors du projet) voit, mais ne décide pas.
                return TypedResults.Forbid();
        }

        // Une seule requête SQL qui vérifie et écrit en même temps : deux décisions simultanées
        // ne peuvent pas passer toutes les deux, et une décision ne vaut que pour la demande affichée.
        var updated = await db.Missions
            .Where(m => m.Id == id && m.Status == MissionStatus.AwaitingApproval && m.Decision == null
                        && m.ApprovalId == request.ApprovalId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Decision, request.Approved), cancellationToken);
        if (updated == 0)
            return TypedResults.Conflict();

        await audit.AppendAsync(id, request.Approved ? "decision:approuvee" : "decision:refusee", cancellationToken);
        await queue.Writer.WriteAsync(id, cancellationToken);
        return TypedResults.Accepted($"/missions/{id}");
    }

    /// <summary>
    /// Répondre à l'agent (leçon 117) : seulement sur une mission terminée qui a gardé sa conversation, par son auteur.
    /// La conversation peut contenir ses compétences à lui (leçon 104) : un autre éditeur pourrait la faire réciter.
    /// Une seule requête vérifie et écrit : deux réponses simultanées ne relancent pas deux fois la mission.
    /// </summary>
    private static async Task<Results<Accepted, NotFound, Conflict<string>, ForbidHttpResult, ValidationProblem>> ReplyAsync(
        Guid id, ReplyRequest request, MissionsDb db, AuditLog audit, MissionQueue queue, ICurrentUser me, IProjectAccess projects,
        IHubContext<MissionsHub> hub, IProjectDirectory directory, IAgentCatalog agents, CancellationToken cancellationToken)
    {
        var text = (request.Text ?? "").Trim();
        if (text.Length is 0 or > MaxGoalLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["text"] = [$"De 1 à {MaxGoalLength} caractères."] });
        if (await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken) is not { } mission)
            return TypedResults.NotFound();
        switch (await AccessAsync(mission, me, projects, cancellationToken))
        {
            case null: return TypedResults.NotFound();
            case ProjectRole.Reader: return TypedResults.Forbid();
        }
        if (mission.CreatedBy != me.Id)
            return TypedResults.Forbid();
        // Comme pour une relance : l'agent de la mission doit être encore proposé (ni retiré, ni d'un autre).
        if (mission.Agent is { } agent && !(await agents.RoleKeysAsync(cancellationToken)).Contains(agent))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["agent"] = ["Cet agent n'est plus proposé : lance une nouvelle mission."] });
        var updated = await db.Missions
            .Where(m => m.Id == id && m.Status == MissionStatus.Completed && m.Conversation != null && m.CreatedBy == me.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MissionStatus.Pending).SetProperty(m => m.FollowUp, text), cancellationToken);
        if (updated == 0)
            return TypedResults.Conflict("On répond à une mission terminée qui a gardé sa conversation : sinon, lance une nouvelle mission.");
        db.Turns.Add(new MissionTurn { MissionId = id, FromUser = true, Text = text, At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        await audit.AppendAsync(id, "reponse", cancellationToken);
        var readers = await directory.ReadersAsync(mission.ProjectId ?? Guid.Empty, cancellationToken);
        await hub.Clients.Users(readers).SendAsync(MissionsHubProtocol.MissionEvent,
            new MissionEventDto(id, MissionStatus.Pending, DateTimeOffset.UtcNow), cancellationToken);
        await queue.Writer.WriteAsync(id, cancellationToken);
        return TypedResults.Accepted($"/missions/{id}");
    }

    private static async Task<Results<Ok<MissionTurnDto[]>, NotFound>> GetTurnsAsync(
        Guid id, MissionsDb db, ICurrentUser me, IProjectAccess projects, CancellationToken cancellationToken)
    {
        if (await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken) is not { } mission
            || await AccessAsync(mission, me, projects, cancellationToken) is null)
            return TypedResults.NotFound();
        // Les identifiants croissent : leur ordre est celui de la conversation (SQLite ne trie pas les dates avec fuseau).
        return TypedResults.Ok(await db.Turns.AsNoTracking().Where(t => t.MissionId == id).OrderBy(t => t.Id)
            .Select(t => new MissionTurnDto(t.FromUser, t.Text, t.At)).ToArrayAsync(cancellationToken));
    }

    /// <summary>
    /// Arrêter une mission (éditeur du projet). En file ou en pause : elle passe tout de suite à « Arrêtée ».
    /// En cours : son exécution est annulée, et le runner la termine en « Arrêtée ». Déjà finie : 409.
    /// </summary>
    private static async Task<Results<Accepted, NotFound, Conflict, ForbidHttpResult>> CancelAsync(
        Guid id, MissionsDb db, AuditLog audit, ICurrentUser me, IProjectAccess projects, IProjectDirectory directory,
        MissionCancellations cancellations, IHubContext<MissionsHub> hub, CancellationToken cancellationToken)
    {
        if (await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken) is not { } mission)
            return TypedResults.NotFound();
        switch (await AccessAsync(mission, me, projects, cancellationToken))
        {
            case null:
                return TypedResults.NotFound();
            case ProjectRole.Reader:
                return TypedResults.Forbid();
        }

        if (cancellations.Cancel(id))
        {
            await audit.AppendAsync(id, "arret-demande", cancellationToken);
            return TypedResults.Accepted($"/missions/{id}");
        }

        // Pas en cours d'exécution : en file ou en pause. Une seule requête vérifie et écrit (pas de double arrêt).
        var updated = await db.Missions
            .Where(m => m.Id == id && (m.Status == MissionStatus.Pending || (m.Status == MissionStatus.AwaitingApproval && m.Decision == null)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MissionStatus.Cancelled)
                .SetProperty(m => m.Conversation, (string?)null)
                .SetProperty(m => m.PendingApproval, (string?)null)
                .SetProperty(m => m.ApprovalId, (Guid?)null)
                .SetProperty(m => m.Result, "Arrêtée à la demande d'un humain."), cancellationToken);
        if (updated == 0)
            return TypedResults.Conflict();

        await audit.AppendAsync(id, nameof(MissionStatus.Cancelled), cancellationToken);
        await hub.Clients.Users(await directory.ReadersAsync(mission.ProjectId ?? Guid.Empty, cancellationToken)).SendAsync(
            MissionsHubProtocol.MissionEvent, new MissionEventDto(id, MissionStatus.Cancelled, DateTimeOffset.UtcNow), cancellationToken);
        return TypedResults.Accepted($"/missions/{id}");
    }

    /// <summary>
    /// Relancer une mission échouée ou arrêtée : une NOUVELLE mission, même but, même type, même projet, liée à l'originale.
    /// L'originale reste telle quelle, avec son journal : on ne réécrit pas l'histoire.
    /// </summary>
    private static async Task<Results<Accepted<MissionDto>, NotFound, Conflict, ForbidHttpResult, ValidationProblem>> RetryAsync(
        Guid id, MissionsDb db, MissionQueue queue, ICurrentUser me, IProjectAccess projects, IProjectDirectory directory,
        IAgentCatalog agents, CancellationToken cancellationToken)
    {
        if (await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken) is not { } original)
            return TypedResults.NotFound();
        switch (await AccessAsync(original, me, projects, cancellationToken))
        {
            case null:
                return TypedResults.NotFound();
            case ProjectRole.Reader:
                return TypedResults.Forbid();
        }
        // Une mission antérieure aux projets n'a pas de projet où relancer : seul un admin la voit, et il la relance à la main.
        if (original.Status is not (MissionStatus.Failed or MissionStatus.Cancelled) || original.ProjectId is not { } projectId)
            return TypedResults.Conflict();

        // Une relance est un nouveau départ : son agent doit être encore proposé (seule une mission en pause finit avec un agent retiré).
        if (original.Agent is { } agent && !(await agents.RoleKeysAsync(cancellationToken)).Contains(agent))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["agent"] = [$"L'agent « {agent} » a été retiré : lancez une nouvelle mission avec un autre agent."],
            });

        var retry = new Mission
        {
            Id = Guid.CreateVersion7(),
            Goal = original.Goal,
            Kind = original.Kind,
            Agent = original.Agent,
            Status = MissionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            ProjectId = projectId,
            CreatedBy = me.Id,
            CreatedByName = me.Username,
            RetryOf = original.Id,
        };
        db.Missions.Add(retry);
        await db.SaveChangesAsync(cancellationToken);
        await queue.Writer.WriteAsync(retry.Id, cancellationToken);
        var names = await directory.NamesAsync([projectId], cancellationToken);
        return TypedResults.Accepted($"/missions/{retry.Id}", retry.ToDto(names.GetValueOrDefault(projectId)));
    }

    private static async Task<Results<Ok<AuditEntryDto[]>, NotFound>> GetEventsAsync(
        Guid id, MissionsDb db, AuditLog audit, ICurrentUser me, IProjectAccess projects, CancellationToken cancellationToken) =>
        await db.Missions.FindAsync([id], cancellationToken) is { } mission && await AccessAsync(mission, me, projects, cancellationToken) is not null
            ? TypedResults.Ok(await audit.ReadAsync(id, cancellationToken))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<MissionDto>, NotFound>> GetAsync(
        Guid id, MissionsDb db, ICurrentUser me, IProjectAccess projects, IProjectDirectory directory, CancellationToken cancellationToken)
    {
        if (await db.Missions.FindAsync([id], cancellationToken) is not { } mission || await AccessAsync(mission, me, projects, cancellationToken) is null)
            return TypedResults.NotFound();
        var name = mission.ProjectId is { } p ? (await directory.NamesAsync([p], cancellationToken)).GetValueOrDefault(p) : null;
        return TypedResults.Ok(mission.ToDto(name));
    }
}
