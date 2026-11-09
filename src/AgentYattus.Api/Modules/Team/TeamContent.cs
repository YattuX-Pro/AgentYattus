using System.Text.Json;
using System.Text.RegularExpressions;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Identity;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Team;

/// <summary>Un type d'événement que la scène sait jouer : qui parle, et avec quelles variables.</summary>
internal sealed record SceneEvent(string Event, string Description, string Speaker, string DefaultTemplate, IReadOnlyList<string> Variables);

public static partial class TeamModule
{
    private static readonly string[] People = ["chef", "analyste", "redacteur", "vous"];

    /// <summary>
    /// Les événements que la scène joue, et leur réplique de départ. Le code décide qui parle (comportement) ;
    /// le texte se change dans l'admin (contenu). Une réplique de départ sert tant que personne ne l'a modifiée.
    /// </summary>
    internal static readonly IReadOnlyList<SceneEvent> SceneEvents =
    [
        new("mission:debut", "La mission démarre", "chef", "On démarre : {objectif}", ["objectif", .. People]),
        new("agent:analyste", "L'analyste prend la main", "chef", "{analyste}, à toi : explore le dossier.", People),
        new("outil:list_files", "Un agent parcourt un dossier", "analyste", "Je parcours {fichier}.", ["fichier", .. People]),
        new("outil:read_file", "Un agent lit un fichier", "analyste", "Je lis {fichier}.", ["fichier", .. People]),
        new("outil:search", "Un agent cherche dans le code", "analyste", "Je cherche « {fichier} » dans le code.", ["fichier", .. People]),
        new("outil:run_command", "Un agent lance une commande", "analyste", "Je lance « {fichier} » dans le bac à sable.", ["fichier", .. People]),
        new("outil:use_skill", "Un agent ouvre une de ses compétences", "analyste", "J'ouvre ma fiche « {fichier} ».", ["fichier", .. People]),
        new("passage", "L'analyste passe le relais au rédacteur", "analyste", "{redacteur}, je t'envoie mes constats.", People),
        new("approbation", "Le rédacteur demande l'accord pour écrire", "redacteur", "Je peux écrire {fichier} ? Il me faut ton accord, {vous}.", ["fichier", .. People]),
        new("decision:approuvee", "L'écriture est approuvée", "redacteur", "Merci ! J'écris {fichier}.", ["fichier", .. People]),
        new("decision:refusee", "L'écriture est refusée", "redacteur", "Compris, je n'écris rien.", People),
        new("competence:proposee", "Un agent propose de retenir une compétence", "analyste",
            "J'ai appris quelque chose : je le note dans une fiche « {fichier} » ? Il me faut ton accord, {vous}.", ["fichier", .. People]),
        new("competence:retenue", "La compétence est retenue", "analyste", "C'est noté, je m'en servirai la prochaine fois.", People),
        new("limite-de-tours", "Un agent atteint sa limite de tours", "analyste", "J'ai atteint ma limite de tours : je rends ce que j'ai.", People),
        new("mission:fin", "La mission est terminée", "chef", "Mission terminée. Beau travail, l'équipe.", People),
        new("mission:echec", "La mission échoue", "chef", "La mission a échoué : on regarde pourquoi.", People),
        new("mission:arret", "La mission est arrêtée", "chef", "Mission arrêtée à ta demande.", People),
    ];

    /// <summary>Les traits proposés tant qu'un administrateur n'a rien changé.</summary>
    internal static readonly IReadOnlyList<TraitDto> DefaultTraits =
    [
        new("rigueur", "Rigoureux", "Tu vérifies chaque affirmation dans les fichiers avant de l'écrire."),
        new("concision", "Concis", "Tu vas droit au but : des phrases courtes, sans répétition."),
        new("pedagogie", "Pédagogue", "Tu expliques pourquoi un point compte, en termes simples."),
        new("curiosite", "Curieux", "Tu regardes aussi les fichiers voisins qui pourraient éclairer le problème."),
        new("prudence", "Prudent", "Tu signales ce qui est risqué avant ce qui est joli."),
    ];

    internal const int DefaultTypingMs = 38;
    internal const int DefaultPauseMs = 1800;

    private static void MapContent(IEndpointRouteBuilder app)
    {
        // Traits : lus par tous (le bureau les propose), changés par les administrateurs.
        app.MapGet(TeamProtocol.Traits, async (TeamDb db, CancellationToken ct) => TypedResults.Ok(await TraitsAsync(db, ct)));

        app.MapPut(TeamProtocol.Traits, async Task<Results<NoContent, ValidationProblem>> (
            UpdateTraitsRequest request, TeamDb db, CancellationToken ct) =>
        {
            var traits = (request.Traits ?? []).Select(t => new TraitDto((t.Key ?? "").Trim().ToLowerInvariant(), (t.Label ?? "").Trim(), (t.Style ?? "").Trim())).ToList();
            var ok = traits.Count is >= 1 and <= 20
                     && traits.Select(t => t.Key).Distinct().Count() == traits.Count
                     && traits.All(t => TraitKeyPattern().IsMatch(t.Key) && t.Label.Length is >= 1 and <= 30 && t.Style.Length is >= 10 and <= 300);
            if (!ok)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["traits"] = ["De 1 à 20 traits ; une clé unique (lettres minuscules, chiffres, tirets), un libellé de 30 caractères au plus, une phrase de 10 à 300 caractères."],
                });
            db.Traits.RemoveRange(db.Traits);
            db.Traits.AddRange(traits.Select((t, i) => new Trait { Key = t.Key, Label = t.Label, Style = t.Style, Order = i }));
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Authentication.LlmAdminPolicy);

        app.MapGet(TeamProtocol.Lines, async (TeamDb db, CancellationToken ct) =>
        {
            var custom = await db.Lines.AsNoTracking().ToDictionaryAsync(l => l.Event, l => l.Template, ct);
            return TypedResults.Ok(SceneEvents.Select(e => new SceneLineDto(
                e.Event, e.Description, e.Speaker, custom.GetValueOrDefault(e.Event) ?? e.DefaultTemplate, e.Variables,
                !custom.ContainsKey(e.Event))).ToList());
        });

        app.MapPut(TeamProtocol.Lines, async Task<Results<NoContent, ValidationProblem>> (
            UpdateSceneLinesRequest request, TeamDb db, ICurrentUser me, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var templates = request.Templates ?? new Dictionary<string, string?>();
            foreach (var (key, template) in templates)
            {
                if (SceneEvents.FirstOrDefault(e => e.Event == key) is not { } known)
                    errors[key] = ["Événement inconnu."];
                else if (!string.IsNullOrWhiteSpace(template) && CheckTemplate(template.Trim(), known.Variables) is { } problem)
                    errors[key] = [problem];
            }

            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            foreach (var (key, template) in templates)
            {
                var row = await db.Lines.FindAsync([key], ct);
                if (string.IsNullOrWhiteSpace(template))
                {
                    if (row is not null)
                        db.Lines.Remove(row);
                    continue;
                }

                if (row is null)
                    db.Lines.Add(row = new SceneLine { Event = key, Template = template.Trim(), UpdatedBy = me.Username });
                row.Template = template.Trim();
                row.UpdatedBy = me.Username;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Authentication.LlmAdminPolicy);

        app.MapGet(TeamProtocol.Settings, async (TeamDb db, CancellationToken ct) => TypedResults.Ok(await SettingsAsync(db, ct)));

        app.MapPut(TeamProtocol.Settings, async Task<Results<NoContent, ValidationProblem>> (
            TeamSettingsDto request, TeamDb db, ICurrentUser me, IAgentCatalog catalog, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var defaultTeam = request.DefaultTeam ?? [];
            if (request.TypingMs is < 5 or > 200)
                errors["typingMs"] = ["Entre 5 et 200 ms par lettre."];
            if (request.PauseMs is < 300 or > 10_000)
                errors["pauseMs"] = ["Entre 300 et 10 000 ms."];
            var roles = await catalog.RoleKeysAsync(ct);
            var portraitIds = new[] { request.ManagerPortraitId }.OfType<Guid>()
                .Concat(defaultTeam.Select(s => s.PortraitId)).Distinct().ToList();
            var known = await db.Portraits.Where(p => portraitIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
            if (known.Count != portraitIds.Count)
                errors["portraits"] = ["Un portrait choisi n'existe pas."];
            if (defaultTeam.Count > roles.Count
                || defaultTeam.Select(s => s.Role).Distinct().Count() != defaultTeam.Count
                || defaultTeam.Any(s => s.Role is null || !roles.Contains(s.Role) || !ValidName((s.Name ?? "").Trim())))
                errors["defaultTeam"] = ["Un siège par rôle existant, avec un prénom valide."];
            else if (defaultTeam.Count(s => s.Leader) > 1)
                errors["defaultTeam"] = ["Un seul chef d'équipe."];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            var row = await db.Settings.FindAsync([TeamSettingsRow.SingletonId], ct);
            if (row is null)
                db.Settings.Add(row = new TeamSettingsRow { Id = TeamSettingsRow.SingletonId });
            row.ManagerPortraitId = request.ManagerPortraitId;
            row.DefaultTeam = JsonSerializer.Serialize(defaultTeam.Select(s => s with { Name = s.Name.Trim() }), JsonSerializerOptions.Web);
            row.TypingMs = request.TypingMs;
            row.PauseMs = request.PauseMs;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = me.Username;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Authentication.LlmAdminPolicy);
    }

    internal static async Task<List<TraitDto>> TraitsAsync(TeamDb db, CancellationToken ct)
    {
        var rows = await db.Traits.AsNoTracking().OrderBy(t => t.Order).Select(t => new TraitDto(t.Key, t.Label, t.Style)).ToListAsync(ct);
        return rows.Count > 0 ? rows : [.. DefaultTraits];
    }

    internal static async Task<Dictionary<string, string>> LinesAsync(TeamDb db, CancellationToken ct)
    {
        var custom = await db.Lines.AsNoTracking().ToDictionaryAsync(l => l.Event, l => l.Template, ct);
        return SceneEvents.ToDictionary(e => e.Event, e => custom.GetValueOrDefault(e.Event) ?? e.DefaultTemplate);
    }

    internal static async Task<TeamSettingsDto> SettingsAsync(TeamDb db, CancellationToken ct)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == TeamSettingsRow.SingletonId, ct);
        var team = row?.DefaultTeam is { } json ? JsonSerializer.Deserialize<List<DefaultSeatDto>>(json, JsonSerializerOptions.Web) ?? [] : [];
        return new TeamSettingsDto(row?.ManagerPortraitId, team,
            row?.TypingMs ?? DefaultTypingMs, row?.PauseMs ?? DefaultPauseMs);
    }

    /// <summary>Une réplique : 160 caractères au plus, et seulement les variables que l'événement fournit.</summary>
    internal static string? CheckTemplate(string template, IReadOnlyList<string> variables)
    {
        if (template.Length > 160)
            return "160 caractères au plus.";
        var unknown = VariablePattern().Matches(template).Select(m => m.Groups[1].Value).Where(v => !variables.Contains(v)).Distinct().ToList();
        return unknown.Count > 0
            ? $"Variable inconnue : {string.Join(", ", unknown.Select(v => "{" + v + "}"))}. Disponibles : {string.Join(", ", variables.Select(v => "{" + v + "}"))}."
            : null;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,29}$")]
    private static partial Regex TraitKeyPattern();

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex VariablePattern();
}
