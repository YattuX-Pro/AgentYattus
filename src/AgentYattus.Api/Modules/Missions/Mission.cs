using System.Text.Json;
using AgentYattus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Missions;

internal sealed class Mission
{
    public Guid Id { get; set; }
    public required string Goal { get; set; }
    public MissionStatus Status { get; set; }
    public MissionKind Kind { get; set; }

    /// <summary>L'agent qui fait une mission « Question » (ADR 0013) ; null : l'Analyste.</summary>
    public string? Agent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Result { get; set; }

    // À qui appartient la mission (ADR 0009). Null pour une mission antérieure aux projets.
    public Guid? ProjectId { get; set; }
    public string? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }

    // Ce qu'a consommé la mission, toutes exécutions confondues (ADR 0010). Coût en micro-dollars : un entier.
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CostMicros { get; set; }

    /// <summary>La mission échouée ou arrêtée dont celle-ci est la relance.</summary>
    public Guid? RetryOf { get; set; }

    // Mission en pause : la conversation de l'agent (opaque pour ce module), ce qui attend une décision, et la décision.
    public string? Conversation { get; set; }
    public string? PendingApproval { get; set; }
    public Guid? ApprovalId { get; set; }
    public bool? Decision { get; set; }

    public MissionDto ToDto(string? projectName = null) => new(
        Id, Goal, Status, CreatedAt, Result,
        PendingApproval is null ? null : JsonSerializer.Deserialize<ApprovalDto>(PendingApproval, JsonSerializerOptions.Web),
        Kind, ProjectId, projectName, CreatedByName, InputTokens, OutputTokens, CostMicros / 1_000_000m, RetryOf, Agent);
}

internal sealed class MissionsDb(DbContextOptions<MissionsDb> options) : DbContext(options)
{
    public const string Schema = "missions";

    public DbSet<Mission> Missions => Set<Mission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Mission>().Property(m => m.Status).HasConversion<string>();
        modelBuilder.Entity<Mission>().Property(m => m.Kind).HasConversion<string>().HasDefaultValue(MissionKind.Question);
        modelBuilder.Entity<Mission>().Property(m => m.CreatedBy).HasMaxLength(64);
        modelBuilder.Entity<Mission>().Property(m => m.CreatedByName).HasMaxLength(100);
        modelBuilder.Entity<Mission>().Property(m => m.Agent).HasMaxLength(40);
        modelBuilder.Entity<Mission>().HasIndex(m => m.ProjectId);
    }
}
