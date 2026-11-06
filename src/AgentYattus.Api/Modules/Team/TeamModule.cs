using System.Text.RegularExpressions;
using AgentYattus.Api.Modules.Identity;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Team;

/// <summary>Les collègues (ADR 0012) : la galerie, l'équipe de chaque projet, les traits, les répliques et la scène.</summary>
public static partial class TeamModule
{
    /// <summary>Une pose au plus : 2 Mo. La galerie livrée pèse 40 à 50 Ko par pose.</summary>
    public const int MaxImageBytes = 2 * 1024 * 1024;

    public static IServiceCollection AddTeamModule(this IServiceCollection services) =>
        services.AddScoped<ITeamDirectory, TeamDirectory>().AddModuleDb<TeamDb>(TeamDb.Schema);

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        MapPortraits(app);
        MapContent(app);
        MapColleagues(app);
        return app;
    }

    private static void MapPortraits(IEndpointRouteBuilder app)
    {
        var portraits = app.MapGroup(TeamProtocol.Portraits);

        // La galerie : tout le monde la voit (pour recruter) ; les portraits désactivés, les seuls administrateurs.
        portraits.MapGet("/", async (TeamDb db, ICurrentUser me, bool? all, CancellationToken ct) =>
        {
            var query = db.Portraits.AsNoTracking();
            if (!(all == true && me.IsInstanceAdmin))
                query = query.Where(p => p.Active);
            var rows = await query.OrderBy(p => p.Slug)
                .Select(p => new { p.Id, p.Slug, p.SuggestedName, p.Active, HasTalk = p.Talk != null })
                .ToListAsync(ct);
            return TypedResults.Ok(rows.Select(p => ToDto(p.Id, p.Slug, p.SuggestedName, p.Active, p.HasTalk)).ToList());
        });

        // Les images sont des illustrations, pas des données : lisibles sans jeton, pour qu'une balise <img> les affiche.
        portraits.MapGet("/{id:guid}/{pose}", async Task<Results<FileContentHttpResult, NotFound>> (
            Guid id, string pose, TeamDb db, HttpContext http, CancellationToken ct) =>
        {
            if (!TeamProtocol.Poses.Contains(pose))
                return TypedResults.NotFound();
            var image = await db.Portraits.AsNoTracking().Where(p => p.Id == id)
                .Select(p => pose == "parle" && p.Talk != null
                    ? new { Bytes = p.Talk, Type = p.TalkType!, p.UpdatedAt }
                    : new { Bytes = p.Work, Type = p.WorkType, p.UpdatedAt })
                .FirstOrDefaultAsync(ct);
            if (image is null)
                return TypedResults.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=3600";
            // Un portrait remplacé change d'étiquette : les navigateurs ne gardent pas l'ancienne image.
            return TypedResults.File(image.Bytes, image.Type,
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{image.UpdatedAt.UtcTicks}\""));
        }).AllowAnonymous();

        var admin = portraits.MapGroup("").RequireAuthorization(Authentication.LlmAdminPolicy);

        admin.MapPost("/", async Task<Results<Created<PortraitDto>, ValidationProblem, Conflict>> (
            [FromForm] string slug, [FromForm] string name, IFormFile travail, IFormFile? parle,
            TeamDb db, ICurrentUser me, CancellationToken ct) =>
        {
            slug = slug.Trim().ToLowerInvariant();
            name = name.Trim();
            var errors = new Dictionary<string, string[]>();
            if (!SlugPattern().IsMatch(slug))
                errors["slug"] = ["De 1 à 40 caractères : lettres minuscules, chiffres et tirets."];
            if (!ValidName(name))
                errors["name"] = ["De 1 à 24 caractères : lettres, espaces, apostrophes et tirets."];
            var work = await ReadImageAsync(travail, ct);
            if (work.Error is { } workError)
                errors["travail"] = [workError];
            var talk = parle is null ? default : await ReadImageAsync(parle, ct);
            if (talk.Error is { } talkError)
                errors["parle"] = [talkError];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            if (await db.Portraits.AnyAsync(p => p.Slug == slug, ct))
                return TypedResults.Conflict();

            var now = DateTimeOffset.UtcNow;
            var portrait = new Portrait
            {
                Id = Guid.CreateVersion7(), Slug = slug, SuggestedName = name, Work = work.Bytes!, WorkType = work.Type!,
                Talk = talk.Bytes, TalkType = talk.Type, CreatedAt = now, UpdatedAt = now, CreatedBy = me.Username,
            };
            db.Portraits.Add(portrait);
            await db.SaveChangesAsync(ct);
            var dto = ToDto(portrait.Id, slug, name, true, talk.Bytes is not null);
            return TypedResults.Created(TeamProtocol.Portrait(portrait.Id), dto);
        }).DisableAntiforgery();

        admin.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound, ValidationProblem>> (
            Guid id, UpdatePortraitRequest request, TeamDb db, CancellationToken ct) =>
        {
            var name = request.SuggestedName.Trim();
            if (!ValidName(name))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["suggestedName"] = ["De 1 à 24 caractères : lettres, espaces, apostrophes et tirets."],
                });
            var portrait = await db.Portraits.FindAsync([id], ct);
            if (portrait is null)
                return TypedResults.NotFound();
            portrait.SuggestedName = name;
            portrait.Active = request.Active;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        // Un portrait utilisé (par un collègue, même ancien, ou par les réglages) se désactive, il ne se supprime pas.
        admin.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound, Conflict<string>>> (Guid id, TeamDb db, CancellationToken ct) =>
        {
            var portrait = await db.Portraits.FindAsync([id], ct);
            if (portrait is null)
                return TypedResults.NotFound();
            var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
            var used = await db.Colleagues.AnyAsync(c => c.PortraitId == id, ct)
                       || settings?.LeaderPortraitId == id || settings?.ManagerPortraitId == id
                       || (settings?.DefaultTeam?.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase) ?? false);
            if (used)
                return TypedResults.Conflict("Ce portrait est utilisé : désactive-le plutôt.");
            db.Portraits.Remove(portrait);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });
    }

    internal static PortraitDto ToDto(Guid id, string slug, string name, bool active, bool hasTalk) => new(
        id, slug, name, active, hasTalk, TeamProtocol.Image(id, "travail"), TeamProtocol.Image(id, hasTalk ? "parle" : "travail"));

    internal static bool ValidName(string name) => NamePattern().IsMatch(name);

    /// <summary>
    /// On croit la signature du fichier, jamais son extension ni le type annoncé par le navigateur :
    /// seuls PNG et WebP passent, et la taille est bornée avant lecture complète.
    /// </summary>
    internal static async Task<(byte[]? Bytes, string? Type, string? Error)> ReadImageAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0)
            return (null, null, "Fichier vide.");
        if (file.Length > MaxImageBytes)
            return (null, null, "2 Mo au plus.");
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        return ImageType(bytes) is { } type ? (bytes, type, null) : (null, null, "PNG ou WebP seulement.");
    }

    internal static string? ImageType(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
        : bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex SlugPattern();

    // Un prénom entre dans la consigne de l'agent : seulement des lettres, pour qu'il ne puisse rien y glisser d'autre.
    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}' \-]{0,23}$")]
    private static partial Regex NamePattern();
}
