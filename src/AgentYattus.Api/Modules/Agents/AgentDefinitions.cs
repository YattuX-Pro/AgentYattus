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

internal sealed class AgentsDb(DbContextOptions<AgentsDb> options) : DbContext(options)
{
    public const string Schema = "agents";

    public DbSet<RoleVersion> Versions => Set<RoleVersion>();

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
    }
}
