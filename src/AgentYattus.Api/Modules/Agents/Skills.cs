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

    /// <summary>La version en vigueur (leçon 132) : chaque modification en crée une, gardée dans SkillVersions.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Brouillon (importée : aucun agent ne la lit avant validation), approuvée, ou dépréciée.</summary>
    public SkillStatus Status { get; set; } = SkillStatus.Approved;

    /// <summary>Ses références, en JSON : [{ kind, location, note }].</summary>
    public string Sources { get; set; } = "";

    public IReadOnlyList<SkillSourceDto> SourceList =>
        Sources.Length == 0 ? [] : System.Text.Json.JsonSerializer.Deserialize<List<SkillSourceDto>>(Sources, System.Text.Json.JsonSerializerOptions.Web) ?? [];

    /// <summary>
    /// Ce que l'agent reçoit en la chargeant : la procédure, puis ses références, à consulter seulement au besoin.
    /// Une compétence ne donne aucun droit : les outils restent ceux du rôle, et les règles de sécurité passent avant.
    /// </summary>
    public string ForAgent() =>
        (Status == SkillStatus.Deprecated ? "(Compétence dépréciée : préfère une autre méthode si tu en as une.)\n" : "") + Body +
        (SourceList.Count == 0 ? "" : "\n\nRéférences (à consulter au besoin : read_file pour le dépôt, read_documentation pour le web) :\n" +
            string.Join('\n', SourceList.Select(r => $"- {r.Kind} : {r.Location}{(r.Note is { Length: > 0 } n ? $" ({n})" : "")}")));
}

/// <summary>Une version d'une compétence, telle qu'elle était : de quoi savoir ce qu'une mission a lu, et revenir en arrière.</summary>
internal sealed class SkillVersion
{
    public required string Key { get; set; }
    public int Version { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Body { get; set; }
    public string Sources { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
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
        new(s.Key, s.Name, s.Description, s.Hidden && !admin ? "" : s.Body, s.OwnerId is not null && s.OwnerId == me, s.Hidden, s.Uses, s.LastUsedAt,
            s.Version, s.Status, s.SourceList);

    /// <summary>
    /// Les références : dix au plus. Un fichier du dépôt est un chemin relatif sans « .. » ; une page est en https, sur un site de
    /// documentation autorisé (la même liste que read_documentation).
    /// </summary>
    internal static string? CheckSources(IReadOnlyList<SkillSourceDto> sources, IReadOnlyList<string> hosts)
    {
        if (sources.Count > 10)
            return "Dix références au plus.";
        foreach (var source in sources)
        {
            var location = (source.Location ?? "").Trim();
            if (location.Length is 0 or > 300 || (source.Note?.Length ?? 0) > 120 || location.Any(char.IsControl))
                return "Une référence fait de 1 à 300 caractères, sa note 120 au plus.";
            if (source.Kind == SkillSourceDto.Repository)
            {
                if (Path.IsPathRooted(location) || location.Split('/', '\\').Any(p => p is ".." or "") || location.Contains(':'))
                    return $"« {location} » : un chemin relatif du dépôt, sans « .. ».";
            }
            else if (source.Kind == SkillSourceDto.Web)
            {
                if (DocumentationReader.CheckUrl(location, hosts) is { } error)
                    return $"« {location} » : {error}";
            }
            else
                return "Une référence est « depot » ou « web ».";
        }
        return null;
    }

    private static List<SkillSourceDto> Clean(IReadOnlyList<SkillSourceDto>? sources) =>
        [.. (sources ?? []).Select(r => new SkillSourceDto((r.Kind ?? "").Trim(), (r.Location ?? "").Trim(), string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim()))];

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
            CreateSkillRequest request, AgentsDb db, ICurrentUser me, Microsoft.Extensions.Options.IOptions<AgentsOptions> options, CancellationToken ct) =>
        {
            var (name, description, body) = ((request.Name ?? "").Trim(), (request.Description ?? "").Trim(), (request.Body ?? "").Trim());
            var sources = Clean(request.Sources);
            var errors = Validate(name, description, body);
            if (CheckSources(sources, options.Value.DocumentationHostList) is { } sourceError)
                errors["sources"] = [sourceError];
            if (SkillReview.Secrets(body + "\n" + description) is { } secret)
                errors["body"] = [secret];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            var personal = request.Personal || !me.IsInstanceAdmin;
            var skill = await SkillLibrary.AddAsync(db, name, description, body, personal ? me.Id : null,
                hidden: request.Hidden && me.IsInstanceAdmin, me.Username, ct, sources: sources);
            return TypedResults.Created(SkillsProtocol.Skill(skill.Key), ToDto(skill, me.Id, me.IsInstanceAdmin));
        });

        // Importer un SKILL.md (leçon 132) : du texte seulement, rien n'est exécuté. Elle arrive en brouillon : aucun agent ne la
        // lit avant que son auteur (ou l'administrateur, pour l'instance) l'ait relue en entier et validée.
        app.MapPost(SkillsProtocol.Import, async Task<Results<Created<SkillImportDto>, ValidationProblem>> (
            ImportSkillRequest request, AgentsDb db, ICurrentUser me, Microsoft.Extensions.Options.IOptions<AgentsOptions> options, CancellationToken ct) =>
        {
            if (SkillFile.Parse(request.Content ?? "") is not { } file)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["content"] = [$"Un SKILL.md de {SkillFile.MaxChars} caractères au plus, qui commence par un en-tête « --- » avec name et description."],
                });
            var errors = Validate(file.Name, file.Description, file.Body);
            if (CheckSources(file.Sources, options.Value.DocumentationHostList) is { } sourceError)
                errors["sources"] = [sourceError];
            if (SkillReview.Secrets(file.Body + "\n" + file.Description) is { } secret)
                errors["body"] = [secret];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            var personal = request.Personal || !me.IsInstanceAdmin;
            var skill = await SkillLibrary.AddAsync(db, file.Name, file.Description, file.Body, personal ? me.Id : null, hidden: false, me.Username, ct,
                status: SkillStatus.Draft, sources: file.Sources);
            return TypedResults.Created(SkillsProtocol.Skill(skill.Key),
                new SkillImportDto(ToDto(skill, me.Id, me.IsInstanceAdmin), [.. SkillReview.Warnings(file.Body), .. file.Notes]));
        });

        // Valider, déprécier : la personne qui peut la modifier. Valider revérifie l'absence de secret.
        app.MapPut("/skills/{key}/status", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            string key, UpdateSkillStatusRequest request, AgentsDb db, ICurrentUser me, CancellationToken ct) =>
        {
            if (await db.Skills.FirstOrDefaultAsync(s => s.Key == key && s.Active, ct) is not { } skill || !Visible(skill, me))
                return TypedResults.NotFound();
            if (!CanEdit(skill, me))
                return TypedResults.Forbid();
            if (request.Status == SkillStatus.Approved && SkillReview.Secrets(skill.Body + "\n" + skill.Description) is { } secret)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["body"] = [secret] });
            skill.Status = request.Status;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        // L'historique : chaque version, telle qu'elle était. Une procédure cachée reste cachée aux non-administrateurs.
        app.MapGet("/skills/{key}/versions", async Task<Results<Ok<List<SkillVersionDto>>, NotFound>> (
            string key, AgentsDb db, ICurrentUser me, CancellationToken ct) =>
        {
            if (await db.Skills.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct) is not { } skill || !Visible(skill, me))
                return TypedResults.NotFound();
            var rows = await db.SkillVersions.AsNoTracking().Where(v => v.Key == key).ToListAsync(ct);
            var hide = skill.Hidden && !me.IsInstanceAdmin;
            return TypedResults.Ok(rows.OrderByDescending(v => v.Version).Select(v => new SkillVersionDto(v.Version, v.Name, v.Description,
                hide ? "" : v.Body, Sources(v.Sources), v.CreatedAt, v.CreatedBy)).ToList());
        });

        // Revenir à une version : une nouvelle version, copie de l'ancienne. L'historique ne s'efface jamais.
        app.MapPost("/skills/{key}/versions/{version:int}/restore", async Task<Results<NoContent, NotFound, ForbidHttpResult>> (
            string key, int version, AgentsDb db, ICurrentUser me, CancellationToken ct) =>
        {
            if (await db.Skills.FirstOrDefaultAsync(s => s.Key == key && s.Active, ct) is not { } skill || !Visible(skill, me))
                return TypedResults.NotFound();
            if (!CanEdit(skill, me))
                return TypedResults.Forbid();
            if (await db.SkillVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Key == key && v.Version == version, ct) is not { } old)
                return TypedResults.NotFound();
            await SkillLibrary.NewVersionAsync(db, skill, old.Name, old.Description, old.Body, old.Sources, me.Username, ct);
            return TypedResults.NoContent();
        });

        // Modifier : une nouvelle version (leçon 132), jamais un écrasement ; les missions passées gardent la leur.
        app.MapPut("/skills/{key}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            string key, UpdateSkillRequest request, AgentsDb db, ICurrentUser me, Microsoft.Extensions.Options.IOptions<AgentsOptions> options,
            CancellationToken ct) =>
        {
            if (await db.Skills.FirstOrDefaultAsync(s => s.Key == key && s.Active, ct) is not { } skill || !Visible(skill, me))
                return TypedResults.NotFound();
            if (!CanEdit(skill, me))
                return TypedResults.Forbid();
            var (name, description, body) = ((request.Name ?? "").Trim(), (request.Description ?? "").Trim(), (request.Body ?? "").Trim());
            var sources = request.Sources is null ? skill.SourceList.ToList() : Clean(request.Sources);
            var errors = Validate(name, description, body);
            if (CheckSources(sources, options.Value.DocumentationHostList) is { } sourceError)
                errors["sources"] = [sourceError];
            if (SkillReview.Secrets(body + "\n" + description) is { } secret)
                errors["body"] = [secret];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            await SkillLibrary.NewVersionAsync(db, skill, name, description, body, Serialize(sources), me.Username, ct);
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

    internal static string Serialize(IReadOnlyList<SkillSourceDto> sources) =>
        sources.Count == 0 ? "" : System.Text.Json.JsonSerializer.Serialize(sources, System.Text.Json.JsonSerializerOptions.Web);

    private static List<SkillSourceDto> Sources(string json) =>
        json.Length == 0 ? [] : System.Text.Json.JsonSerializer.Deserialize<List<SkillSourceDto>>(json, System.Text.Json.JsonSerializerOptions.Web) ?? [];
}

internal static class SkillLibrary
{
    /// <summary>Ajoute une compétence ; son identifiant vient du nom, unique dans toute l'instance (un suffixe sinon).</summary>
    public static async Task<Skill> AddAsync(
        AgentsDb db, string name, string description, string body, string? ownerId, bool hidden, string by, CancellationToken ct,
        SkillStatus status = SkillStatus.Approved, IReadOnlyList<SkillSourceDto>? sources = null)
    {
        var baseKey = AgentsModule.KeyFrom(name);
        var taken = (await db.Skills.AsNoTracking().Where(s => s.Key.StartsWith(baseKey)).Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        var key = baseKey;
        for (var n = 2; taken.Contains(key); n++)
            key = $"{baseKey[..Math.Min(36, baseKey.Length)]}-{n}";
        var skill = new Skill
        {
            Key = key, Name = name, Description = description, Body = body, OwnerId = ownerId, Hidden = hidden,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = by, Status = status, Sources = SkillsEndpoints.Serialize(sources ?? []),
        };
        db.Skills.Add(skill);
        db.SkillVersions.Add(Snapshot(skill, by));
        await db.SaveChangesAsync(ct);
        return skill;
    }

    /// <summary>Une modification : la version suivante, gardée dans l'historique. La version d'avant y est déjà (ou y entre).</summary>
    public static async Task NewVersionAsync(
        AgentsDb db, Skill skill, string name, string description, string body, string sources, string by, CancellationToken ct)
    {
        // Les compétences d'avant la leçon 132 n'ont pas d'historique : leur version 1 y entre avant d'être remplacée.
        if (!await db.SkillVersions.AnyAsync(v => v.Key == skill.Key && v.Version == skill.Version, ct))
            db.SkillVersions.Add(Snapshot(skill, skill.CreatedBy));
        (skill.Name, skill.Description, skill.Body, skill.Sources) = (name, description, body, sources);
        skill.Version++;
        db.SkillVersions.Add(Snapshot(skill, by));
        await db.SaveChangesAsync(ct);
    }

    private static SkillVersion Snapshot(Skill skill, string by) => new()
    {
        Key = skill.Key, Version = skill.Version, Name = skill.Name, Description = skill.Description, Body = skill.Body,
        Sources = skill.Sources, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = by,
    };
}
