using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services) =>
        services
            .AddModuleDb<AuditDb>(AuditDb.Schema)
            .AddScoped(sp => new AuditLog(sp.GetRequiredService<AuditDb>()));

    private const int MaxFeed = 500;

    /// <summary>
    /// Le journal de toutes les missions, les plus récents d'abord. Réservé aux administrateurs :
    /// il révèle ce que font toutes les missions de l'instance.
    /// </summary>
    // ponytail: une seule page ; ajouter un curseur (?before=id) quand 500 lignes ne suffiront plus.
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AuditProtocol.Path, async Task<Results<Ok<AuditFeedEntryDto[]>, ValidationProblem>> (
            AuditDb db, CancellationToken cancellationToken, int limit = 200) =>
        {
            if (limit is < 1 or > MaxFeed)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = [$"La limite est comprise entre 1 et {MaxFeed}."],
                });
            return TypedResults.Ok(await db.Events.OrderByDescending(e => e.Id).Take(limit)
                .Select(e => new AuditFeedEntryDto(e.MissionId, e.Type, e.At)).ToArrayAsync(cancellationToken));
        }).RequireAuthorization(Authentication.LlmAdminPolicy);
        return app;
    }
}

public sealed class AuditLog
{
    private readonly AuditDb _db;

    internal AuditLog(AuditDb db) => _db = db;

    public async Task AppendAsync(Guid missionId, string type, CancellationToken cancellationToken)
    {
        _db.Events.Add(new AuditEvent { MissionId = missionId, Type = type, At = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Le journal d'une mission, dans l'ordre où les événements ont été inscrits.</summary>
    public async Task<AuditEntryDto[]> ReadAsync(Guid missionId, CancellationToken cancellationToken) =>
        await _db.Events.Where(e => e.MissionId == missionId).OrderBy(e => e.Id)
            .Select(e => new AuditEntryDto(e.Type, e.At)).ToArrayAsync(cancellationToken);
}

internal sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid MissionId { get; set; }
    public required string Type { get; set; }
    public DateTimeOffset At { get; set; }
}

internal sealed class AuditDb(DbContextOptions<AuditDb> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuditEvent>().HasIndex(e => e.MissionId);
    }
}
