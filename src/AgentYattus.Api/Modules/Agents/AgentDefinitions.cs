using AgentYattus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>La consigne et la limite de tours en vigueur pour un rôle : la dernière version, sinon celles du code (ADR 0011).</summary>
internal sealed class AgentDefinitions(AgentsDb db)
{
    public async Task<RoleVersion?> CurrentAsync(string role, CancellationToken ct) =>
        await db.Versions.Where(v => v.Role == role).OrderByDescending(v => v.Version).FirstOrDefaultAsync(ct);

    public async Task<List<RoleVersion>> HistoryAsync(string role, CancellationToken ct) =>
        await db.Versions.Where(v => v.Role == role).OrderByDescending(v => v.Version).ToListAsync(ct);

    public async Task SaveAsync(string role, string? instructions, int? maxIterations, string by, CancellationToken ct)
    {
        var next = (await CurrentAsync(role, ct))?.Version + 1 ?? 1;
        db.Versions.Add(new RoleVersion
        {
            Role = role, Version = next, Instructions = instructions, MaxIterations = maxIterations,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = by,
        });
        // Deux enregistrements simultanés : l'index unique (rôle, version) en refuse un, plutôt que d'en garder deux « version 3 ».
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Une version de la consigne d'un rôle. On n'écrase jamais : on ajoute.</summary>
internal sealed class RoleVersion
{
    public long Id { get; set; }
    public required string Role { get; set; }
    public int Version { get; set; }

    /// <summary>Null : la consigne d'origine (celle du code).</summary>
    public string? Instructions { get; set; }

    public int? MaxIterations { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

/// <summary>Un agent créé dans l'admin (ADR 0013). Retiré, il reste là : ses missions passées gardent son nom.</summary>
internal sealed class CustomRole
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Summary { get; set; }

    /// <summary>La consigne d'origine ; ses versions suivantes vont dans RoleVersions, comme pour les rôles du code.</summary>
    public required string Instructions { get; set; }

    /// <summary>Les outils, séparés par des virgules, pris dans WorkspaceTools.Catalog.</summary>
    public required string Tools { get; set; }

    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }

    /// <summary>L'utilisateur à qui l'agent appartient (créé depuis « Nouveau collègue ») ; null : un agent de l'instance, pour tous.</summary>
    public string? OwnerId { get; set; }

    /// <summary>Consigne recopiée d'un poste que son propriétaire ne peut pas lire (BasedOn) : elle sert aux missions, sans lui être montrée.</summary>
    public bool InstructionsHidden { get; set; }

    public IReadOnlyList<string> ToolList => Tools.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

internal sealed class AgentsDb(DbContextOptions<AgentsDb> options) : DbContext(options)
{
    public const string Schema = "agents";

    public DbSet<RoleVersion> Versions => Set<RoleVersion>();
    public DbSet<CustomRole> CustomRoles => Set<CustomRole>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<SkillVersion> SkillVersions => Set<SkillVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<RoleVersion>(e =>
        {
            e.ToTable("RoleVersions");
            e.Property(v => v.Role).HasMaxLength(40);
            e.Property(v => v.Instructions).HasMaxLength(8000);
            e.Property(v => v.CreatedBy).HasMaxLength(100);
            e.HasIndex(v => new { v.Role, v.Version }).IsUnique();
        });
        modelBuilder.Entity<CustomRole>(e =>
        {
            e.HasKey(r => r.Key);
            e.Property(r => r.Key).HasMaxLength(40);
            e.Property(r => r.Name).HasMaxLength(40);
            e.Property(r => r.Summary).HasMaxLength(200);
            e.Property(r => r.Instructions).HasMaxLength(8000);
            e.Property(r => r.Tools).HasMaxLength(200);
            e.Property(r => r.CreatedBy).HasMaxLength(100);
        });
        modelBuilder.Entity<Skill>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(40);
            e.Property(s => s.Name).HasMaxLength(40);
            e.Property(s => s.Description).HasMaxLength(200);
            e.Property(s => s.Body).HasMaxLength(8000);
            e.Property(s => s.OwnerId).HasMaxLength(64);
            e.Property(s => s.CreatedBy).HasMaxLength(100);
            e.Property(s => s.Version).HasDefaultValue(1);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(SkillStatus.Approved);
            e.Property(s => s.Sources).HasMaxLength(4000).HasDefaultValue("");
            e.Ignore(s => s.SourceList);
        });
        modelBuilder.Entity<SkillVersion>(e =>
        {
            e.ToTable("SkillVersions");
            e.HasKey(v => new { v.Key, v.Version });
            e.Property(v => v.Key).HasMaxLength(40);
            e.Property(v => v.Name).HasMaxLength(40);
            e.Property(v => v.Description).HasMaxLength(200);
            e.Property(v => v.Body).HasMaxLength(8000);
            e.Property(v => v.Sources).HasMaxLength(4000);
            e.Property(v => v.CreatedBy).HasMaxLength(100);
        });
    }
}
