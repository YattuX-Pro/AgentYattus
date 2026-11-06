using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Team;

/// <summary>Un visage de la galerie : deux poses, stockées en base (40 à 50 Ko chacune en WebP).</summary>
internal sealed class Portrait
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }
    public required string SuggestedName { get; set; }
    public bool Active { get; set; } = true;
    public required byte[] Work { get; set; }
    public required string WorkType { get; set; }
    public byte[]? Talk { get; set; }
    public string? TalkType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Un collègue d'un projet : un siège par rôle. Remplacé, il reste dans l'historique (Active = false).</summary>
internal sealed class Colleague
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Role { get; set; }
    public Guid PortraitId { get; set; }
    public required string Name { get; set; }

    /// <summary>Les clés des traits, séparées par des virgules.</summary>
    public string Traits { get; set; } = "";

    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public string? RemovedBy { get; set; }

    public IReadOnlyList<string> TraitKeys => Traits.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

internal sealed class Trait
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public required string Style { get; set; }
    public int Order { get; set; }
}

/// <summary>Une réplique modifiée ; un événement sans ligne garde la réplique de départ.</summary>
internal sealed class SceneLine
{
    public required string Event { get; set; }
    public required string Template { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}

/// <summary>Une seule ligne ; chaque colonne nulle reprend la valeur de départ.</summary>
internal sealed class TeamSettingsRow
{
    public const int SingletonId = 1;

    public int Id { get; set; }
    public Guid? LeaderPortraitId { get; set; }
    public string? LeaderName { get; set; }
    public Guid? ManagerPortraitId { get; set; }

    /// <summary>L'équipe de départ, en JSON : [{ role, portraitId, name }].</summary>
    public string? DefaultTeam { get; set; }

    public int? TypingMs { get; set; }
    public int? PauseMs { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

internal sealed class TeamDb(DbContextOptions<TeamDb> options) : DbContext(options)
{
    public const string Schema = "team";

    public DbSet<Portrait> Portraits => Set<Portrait>();
    public DbSet<Colleague> Colleagues => Set<Colleague>();
    public DbSet<Trait> Traits => Set<Trait>();
    public DbSet<SceneLine> Lines => Set<SceneLine>();
    public DbSet<TeamSettingsRow> Settings => Set<TeamSettingsRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Portrait>(e =>
        {
            e.Property(p => p.Slug).HasMaxLength(40);
            e.Property(p => p.SuggestedName).HasMaxLength(24);
            e.Property(p => p.WorkType).HasMaxLength(20);
            e.Property(p => p.TalkType).HasMaxLength(20);
            e.Property(p => p.CreatedBy).HasMaxLength(100);
            e.HasIndex(p => p.Slug).IsUnique();
        });
        modelBuilder.Entity<Colleague>(e =>
        {
            e.Property(c => c.Role).HasMaxLength(40);
            e.Property(c => c.Name).HasMaxLength(24);
            e.Property(c => c.Traits).HasMaxLength(200);
            e.Property(c => c.CreatedBy).HasMaxLength(100);
            e.Property(c => c.RemovedBy).HasMaxLength(100);
            e.HasIndex(c => c.ProjectId, "IX_Colleagues_ProjectId");
            // Un seul collègue actif par rôle et par projet : deux recrutements simultanés ne créent pas deux sièges.
            e.HasIndex(c => new { c.ProjectId, c.Role }, "IX_Colleagues_Seat").IsUnique().HasFilter("\"Active\"");
        });
        modelBuilder.Entity<Trait>(e =>
        {
            e.HasKey(t => t.Key);
            e.Property(t => t.Key).HasMaxLength(30);
            e.Property(t => t.Label).HasMaxLength(30);
            e.Property(t => t.Style).HasMaxLength(300);
        });
        modelBuilder.Entity<SceneLine>(e =>
        {
            e.HasKey(l => l.Event);
            e.Property(l => l.Event).HasMaxLength(60);
            e.Property(l => l.Template).HasMaxLength(160);
            e.Property(l => l.UpdatedBy).HasMaxLength(100);
        });
        modelBuilder.Entity<TeamSettingsRow>(e =>
        {
            // « TeamSettings » et non « Instance » : SQLite (tests) ignore les schémas, et Settings a déjà une table Instance.
            e.ToTable("TeamSettings");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.LeaderName).HasMaxLength(24);
            e.Property(s => s.DefaultTeam).HasMaxLength(4000);
            e.Property(s => s.UpdatedBy).HasMaxLength(100);
        });
    }
}
