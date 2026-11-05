using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services) =>
        services
            .AddModuleDb<AuditDb>(AuditDb.Schema)
            .AddScoped(sp => new AuditLog(sp.GetRequiredService<AuditDb>()));
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
