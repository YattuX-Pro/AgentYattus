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
        app.MapHub<MissionsHub>(MissionsHubProtocol.Path);
        return app;
    }

    private static async Task<Results<Accepted<MissionDto>, ValidationProblem>> CreateAsync(
        CreateMissionRequest request, MissionsDb db, MissionQueue queue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Goal) || request.Goal.Length > MaxGoalLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["goal"] = [$"Le but est obligatoire et fait au plus {MaxGoalLength} caractères."],
            });

        var mission = new Mission
        {
            Id = Guid.CreateVersion7(),
            Goal = request.Goal.Trim(),
            Status = MissionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Missions.Add(mission);
        await db.SaveChangesAsync(cancellationToken);
        await queue.Writer.WriteAsync(mission.Id, cancellationToken);

        return TypedResults.Accepted($"/missions/{mission.Id}", mission.ToDto());
    }

    // ponytail: une seule page, les plus récentes d'abord ; ajouter un curseur (?before=) quand 100 ne suffira plus.
    private static async Task<Results<Ok<MissionDto[]>, ValidationProblem>> ListAsync(
        MissionsDb db, CancellationToken cancellationToken, int limit = DefaultPageSize)
    {
        if (limit is < 1 or > MaxPageSize)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = [$"La limite est comprise entre 1 et {MaxPageSize}."],
            });

        // Les identifiants v7 commencent par l'heure de création : trier par Id, c'est trier par date.
        // (SQLite ne sait pas trier sur un DateTimeOffset.)
        var page = await db.Missions.OrderByDescending(m => m.Id).Take(limit).ToListAsync(cancellationToken);
        return TypedResults.Ok(page.Select(m => m.ToDto()).ToArray());
    }

    private static async Task<Results<Ok<MissionDto>, NotFound>> GetAsync(
        Guid id, MissionsDb db, CancellationToken cancellationToken) =>
        await db.Missions.FindAsync([id], cancellationToken) is { } mission
            ? TypedResults.Ok(mission.ToDto())
            : TypedResults.NotFound();
}
