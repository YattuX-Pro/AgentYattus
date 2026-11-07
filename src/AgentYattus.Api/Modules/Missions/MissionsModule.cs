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
        missions.MapPost("/{id:guid}/cancel", CancelAsync);
        missions.MapPost("/{id:guid}/retry", RetryAsync);
        missions.MapGet("/{id:guid}/events", GetEventsAsync);
        app.MapHub<MissionsHub>(MissionsHubProtocol.Path);
        app.MapGet(AgentsProtocol.Performance, async (MissionsDb db, CancellationToken ct) => TypedResults.Ok(await PerformanceAsync(db, ct)))
            .RequireAuthorization(Authentication.LlmAdminPolicy);
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
