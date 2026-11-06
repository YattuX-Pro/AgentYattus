using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Identity;
using AgentYattus.Api.Modules.Projects;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
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
        missions.MapGet("/{id:guid}/events", GetEventsAsync);
        app.MapHub<MissionsHub>(MissionsHubProtocol.Path);
        return app;
    }

    private static async Task<Results<Accepted<MissionDto>, ValidationProblem, NotFound, ForbidHttpResult>> CreateAsync(
        CreateMissionRequest request, MissionsDb db, MissionQueue queue, ICurrentUser me, IProjectAccess projects,
        IProjectDirectory directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Goal) || request.Goal.Length > MaxGoalLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["goal"] = [$"Le but est obligatoire et fait au plus {MaxGoalLength} caractères."],
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
