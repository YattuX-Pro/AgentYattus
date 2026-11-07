using System.Text.Json;
using AgentYattus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Team;

/// <summary>Le fichier portraits.json d'un dossier de galerie.</summary>
internal sealed record SeedFile(
    IReadOnlyList<SeedPortrait> Portraits, SeedSeat? Leader, string? Manager, IReadOnlyList<SeedSeat>? DefaultTeam);

internal sealed record SeedPortrait(string Slug, string Name);

/// <param name="Role">Vide pour le chef d'équipe.</param>
internal sealed record SeedSeat(string? Role, string Slug, string Name);

/// <summary>
/// Au démarrage, une galerie vide reçoit les portraits du dossier Team:SeedFolder (deploy/portraits) :
/// &lt;slug&gt;.webp (pose « travaille »), &lt;slug&gt;-parle.webp (pose « parle »), et portraits.json pour les prénoms,
/// le chef d'équipe, « Vous » et l'équipe de départ. Une galerie qui contient déjà un portrait n'est jamais touchée.
/// </summary>
internal sealed class PortraitSeeder(
    IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment, ILogger<PortraitSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration["Team:SeedFolder"] is not { Length: > 0 } configured)
            return;
        // Relatif au projet de l'API (là où se trouve appsettings.json), quel que soit le dossier d'où l'on lance.
        var folder = Path.GetFullPath(configured, environment.ContentRootPath);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TeamDb>();
        if (await db.Portraits.AnyAsync(cancellationToken))
            return;
        try
        {
            var count = await SeedAsync(db, folder, cancellationToken);
            logger.LogInformation("Galerie importée depuis {Folder} : {Count} portraits", folder, count);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            // Une galerie absente ou abîmée ne doit pas empêcher l'API de démarrer : elle reste vide, l'admin peut en ajouter.
            logger.LogWarning(ex, "Import de la galerie depuis {Folder} impossible", folder);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task<int> SeedAsync(TeamDb db, string folder, CancellationToken ct)
    {
        var seed = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(Path.Combine(folder, "portraits.json"), ct), JsonSerializerOptions.Web)
                   ?? throw new InvalidDataException("portraits.json vide.");
        var now = DateTimeOffset.UtcNow;
        var bySlug = new Dictionary<string, Guid>();
        foreach (var p in seed.Portraits)
        {
            var work = await ReadAsync(Path.Combine(folder, $"{p.Slug}.webp"), ct)
                       ?? throw new InvalidDataException($"{p.Slug}.webp absent ou illisible.");
            var talk = await ReadAsync(Path.Combine(folder, $"{p.Slug}-parle.webp"), ct);
            var portrait = new Portrait
            {
                Id = Guid.CreateVersion7(), Slug = p.Slug, SuggestedName = p.Name, Work = work.Bytes, WorkType = work.Type,
                Talk = talk?.Bytes, TalkType = talk?.Type, CreatedAt = now, UpdatedAt = now, CreatedBy = "import de la galerie",
            };
            db.Portraits.Add(portrait);
            bySlug[p.Slug] = portrait.Id;
        }

        if (!await db.Settings.AnyAsync(ct))
            db.Settings.Add(new TeamSettingsRow
            {
                Id = TeamSettingsRow.SingletonId,
                LeaderPortraitId = seed.Leader is { } l ? bySlug.GetValueOrDefault(l.Slug) : null,
                LeaderName = seed.Leader?.Name,
                ManagerPortraitId = seed.Manager is { } m ? bySlug.GetValueOrDefault(m) : null,
                DefaultTeam = JsonSerializer.Serialize(
                    (seed.DefaultTeam ?? []).Where(s => s.Role is not null && bySlug.ContainsKey(s.Slug))
                        .Select(s => new DefaultSeatDto(s.Role!, bySlug[s.Slug], s.Name)), JsonSerializerOptions.Web),
                UpdatedAt = now,
                UpdatedBy = "import de la galerie",
            });
        await db.SaveChangesAsync(ct);
        return seed.Portraits.Count;
    }

    // Même contrôle que pour un envoi depuis l'admin : la signature, et 2 Mo au plus.
    private static async Task<(byte[] Bytes, string Type)?> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > TeamModule.MaxImageBytes)
            return null;
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return TeamModule.Check(bytes) is ({ } type, null) ? (bytes, type) : null;
    }
}
