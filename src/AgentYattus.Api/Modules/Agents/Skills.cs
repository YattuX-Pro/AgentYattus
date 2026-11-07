using AgentYattus.Api.Modules.Identity;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>
/// Une compétence (leçon 104) : un savoir-faire écrit. L'agent ne voit que son nom et sa description ;
/// il charge la procédure avec use_skill quand sa tâche en a besoin (chargement progressif).
/// </summary>
internal sealed class Skill
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Body { get; set; }

    /// <summary>L'utilisateur à qui elle appartient ; null : une compétence de l'instance, pour tous.</summary>
    public string? OwnerId { get; set; }

    /// <summary>Procédure publiée par l'administrateur sans être montrée : les agents la lisent, les utilisateurs non.</summary>
    public bool Hidden { get; set; }

    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }

    /// <summary>La mesure : combien de fois un agent l'a chargée, et quand pour la dernière fois.</summary>
    public int Uses { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

internal static class SkillsEndpoints
{
    internal static Dictionary<string, string[]> Validate(string name, string description, string body)
    {
        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 2 or > 40 || !name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-'))
            errors["name"] = ["De 2 à 40 caractères : lettres, chiffres, espaces, apostrophes et tirets."];
        if (description.Length is < 10 or > 200)
            errors["description"] = ["Quand s'en servir, en une phrase de 10 à 200 caractères."];
        if (body.Length is < 20 or > 8000)
            errors["body"] = ["La procédure fait entre 20 et 8 000 caractères."];
        return errors;
    }

    private static SkillDto ToDto(Skill s, string? me, bool admin) =>
        new(s.Key, s.Name, s.Description, s.Hidden && !admin ? "" : s.Body, s.OwnerId is not null && s.OwnerId == me, s.Hidden, s.Uses, s.LastUsedAt);

    public static void Map(IEndpointRouteBuilder app)
    {
        // Les compétences que l'on peut donner à ses collègues : celles de l'instance et les siennes.
        // ?all=true (administrateur) : aussi celles de chaque utilisateur.
        app.MapGet(SkillsProtocol.Path, async (AgentsDb db, ICurrentUser me, bool? all, CancellationToken ct) =>
        {
            var everyone = me.IsInstanceAdmin && all == true;
            var rows = await db.Skills.AsNoTracking()
                .Where(s => s.Active && (everyone || s.OwnerId == null || s.OwnerId == me.Id)).ToListAsync(ct);
            return TypedResults.Ok(rows.OrderBy(s => s.Name, StringComparer.CurrentCulture).Select(s => ToDto(s, me.Id, me.IsInstanceAdmin)).ToList());
        });

        // Tout utilisateur écrit les siennes ; l'administrateur peut publier pour l'instance, et cacher la procédure.
        app.MapPost(SkillsProtocol.Path, async Task<Results<Created<SkillDto>, ValidationProblem>> (
            CreateSkillRequest request, AgentsDb db, ICurrentUser me, CancellationToken ct) =>
        {
            var (name, description, body) = ((request.Name ?? "").Trim(), (request.Description ?? "").Trim(), (request.Body ?? "").Trim());
            var errors = Validate(name, description, body);
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            var personal = request.Personal || !me.IsInstanceAdmin;
            var skill = await SkillLibrary.AddAsync(db, name, description, body, personal ? me.Id : null,
                hidden: request.Hidden && me.IsInstanceAdmin, me.Username, ct);
            return TypedResults.Created(SkillsProtocol.Skill(skill.Key), ToDto(skill, me.Id, me.IsInstanceAdmin));
        });

        app.MapPut("/skills/{key}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            string key, UpdateSkillRequest request, AgentsDb db, ICurrentUser me, CancellationToken ct) =>
        {
            if (await db.Skills.FirstOrDefaultAsync(s => s.Key == key && s.Active, ct) is not { } skill || !Visible(skill, me))
                return TypedResults.NotFound();
            if (!CanEdit(skill, me))
                return TypedResults.Forbid();
            var (name, description, body) = ((request.Name ?? "").Trim(), (request.Description ?? "").Trim(), (request.Body ?? "").Trim());
            var errors = Validate(name, description, body);
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            (skill.Name, skill.Description, skill.Body) = (name, description, body);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        // Retirer : les collègues qui l'avaient ne la chargent plus ; elle reste en base (ses mesures aussi).
        app.MapDelete("/skills/{key}", async Task<Results<NoContent, NotFound, ForbidHttpResult>> (
            string key, AgentsDb db, ICurrentUser me, CancellationToken ct) =>
        {
            if (await db.Skills.FirstOrDefaultAsync(s => s.Key == key && s.Active, ct) is not { } skill || !Visible(skill, me))
                return TypedResults.NotFound();
            if (!CanEdit(skill, me))
                return TypedResults.Forbid();
            skill.Active = false;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });
    }

    // Celle d'un autre utilisateur n'existe pas pour nous (404), comme ses agents.
    private static bool Visible(Skill s, ICurrentUser me) => me.IsInstanceAdmin || s.OwnerId is null || s.OwnerId == me.Id;

    private static bool CanEdit(Skill s, ICurrentUser me) => me.IsInstanceAdmin || (s.OwnerId is not null && s.OwnerId == me.Id);
}

internal static class SkillLibrary
{
    /// <summary>Ajoute une compétence ; son identifiant vient du nom, unique dans toute l'instance (un suffixe sinon).</summary>
    public static async Task<Skill> AddAsync(
        AgentsDb db, string name, string description, string body, string? ownerId, bool hidden, string by, CancellationToken ct)
    {
        var baseKey = AgentsModule.KeyFrom(name);
        var taken = (await db.Skills.AsNoTracking().Where(s => s.Key.StartsWith(baseKey)).Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        var key = baseKey;
        for (var n = 2; taken.Contains(key); n++)
            key = $"{baseKey[..Math.Min(36, baseKey.Length)]}-{n}";
        var skill = new Skill
        {
            Key = key, Name = name, Description = description, Body = body, OwnerId = ownerId, Hidden = hidden,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = by,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync(ct);
        return skill;
    }
}
