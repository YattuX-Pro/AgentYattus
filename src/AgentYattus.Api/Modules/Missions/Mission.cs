using System.Text.Json;
using AgentYattus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Missions;

internal sealed class Mission
{
    public Guid Id { get; set; }
    public required string Goal { get; set; }
    public MissionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Result { get; set; }

    // Mission en pause : la conversation de l'agent (opaque pour ce module), ce qui attend une décision, et la décision.
    public string? Conversation { get; set; }
    public string? PendingApproval { get; set; }
    public bool? Decision { get; set; }

    public MissionDto ToDto() => new(
        Id, Goal, Status, CreatedAt, Result,
        PendingApproval is null ? null : JsonSerializer.Deserialize<ApprovalDto>(PendingApproval, JsonSerializerOptions.Web));
}

internal sealed class MissionsDb(DbContextOptions<MissionsDb> options) : DbContext(options)
{
    public const string Schema = "missions";

    public DbSet<Mission> Missions => Set<Mission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Mission>().Property(m => m.Status).HasConversion<string>();
    }
}
