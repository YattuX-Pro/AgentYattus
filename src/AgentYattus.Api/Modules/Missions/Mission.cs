using AgentYattus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Missions;

internal sealed class Mission
{
    public Guid Id { get; set; }
    public required string Goal { get; set; }
    public MissionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public MissionDto ToDto() => new(Id, Goal, Status, CreatedAt);
}

internal sealed class MissionsDb(DbContextOptions<MissionsDb> options) : DbContext(options)
{
    public DbSet<Mission> Missions => Set<Mission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("missions");
        modelBuilder.Entity<Mission>().Property(m => m.Status).HasConversion<string>();
    }
}
