using AgentYattus.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Api.Modules.Settings;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace AgentYattus.Api.Modules.Agents;

public interface IAgentRunner
{
    /// <param name="resume">La conversation mise en pause et la décision humaine, ou null pour un premier passage.</param>
    /// <param name="workspace">Le dossier de travail du projet ; null pour le dossier configuré (Agents:Workspace).</param>
    /// <param name="seats">Par rôle, le collègue qui l'occupe : sa présentation (ADR 0012) et ses compétences (leçon 104).</param>
    /// <param name="agent">L'agent qui fait une mission « Question » (ADR 0013) ; null : l'Analyste.</param>
    /// <param name="launchedBy">Qui a lancé la mission : ses compétences à lui, et celles de l'instance, sont les seules chargées.</param>
    Task<AgentOutcome> RunAsync(
        Guid missionId, string goal, MissionKind kind, AgentResume? resume, string? workspace,
        IReadOnlyDictionary<string, AgentSeat>? seats, string? agent, string? launchedBy, CancellationToken cancellationToken);
}

/// <summary>Le collègue qui occupe un rôle dans le projet : « Tu t'appelles Inès… », et les clés de ses compétences.</summary>
public sealed record AgentSeat(string Style, IReadOnlyList<string> Skills);

/// <summary>
/// Soit l'agent a fini (<see cref="Answer"/>), soit il attend une décision humaine (<see cref="Approval"/>).
/// Dans ce cas, <see cref="Conversation"/> est l'état à conserver tel quel pour reprendre plus tard.
/// </summary>
/// <param name="Learned">Les compétences approuvées pendant cette exécution, à ranger sur le collègue (leçon 106).</param>
public sealed record AgentOutcome(
    string? Answer, AgentApproval? Approval = null, string? Conversation = null, AgentUsage? Usage = null,
    IReadOnlyList<AgentLearned>? Learned = null);

/// <summary>Une compétence qu'un agent a proposée et qu'un humain a acceptée : elle rejoint le collègue de ce rôle.</summary>
public sealed record AgentLearned(string Role, string Skill);

/// <summary>Ce qu'a consommé une exécution : tokens et coût estimé en micro-dollars (ADR 0010).</summary>
public sealed record AgentUsage(long InputTokens, long OutputTokens, long CostMicros)
{
    public static AgentUsage operator +(AgentUsage a, AgentUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens, a.CostMicros + b.CostMicros);
}

/// <summary>Les actions que l'agent veut faire et qui attendent une décision humaine.</summary>
public sealed record AgentApproval(IReadOnlyList<AgentToolCall> ToolCalls);

public sealed record AgentToolCall(string Tool, string Arguments);

public sealed record AgentResume(string Conversation, bool Approved);

public static class AgentsModule
{
    /// <summary>Les rôles qui travaillent sur une mission de ce type : ceux dont le modèle peut coûter.</summary>
    public static IReadOnlyList<string> RolesFor(MissionKind kind, string? agent = null) =>
        kind == MissionKind.Review ? [AgentRoles.Analyst.Key, AgentRoles.Writer.Key] : [agent ?? AgentRoles.Analyst.Key];

    public static IServiceCollection AddAgentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AgentsOptions>().BindConfiguration(AgentsOptions.Section);
        // Le modèle et sa clé viennent du module Llm (choisis dans l'application) ; voir ILlmClientProvider.
        return services
            .AddScoped<IAgentRunner, AgentFrameworkRunner>()
            .AddScoped<AgentDefinitions>()
            .AddScoped<AgentCatalog>()
            .AddScoped<IAgentCatalog>(sp => sp.GetRequiredService<AgentCatalog>())
            .AddModuleDb<AgentsDb>(AgentsDb.Schema);
    }

    private static (string Name, string Summary, List<string> Tools) Clean(string? name, string? summary, IReadOnlyList<string>? tools) =>
        ((name ?? "").Trim(), (summary ?? "").Trim(), [.. (tools ?? []).Select(t => (t ?? "").Trim()).Distinct()]);

    private static Dictionary<string, string[]> Validate(string name, string summary, List<string> tools)
    {
        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 2 or > 40 || !name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-'))
            errors["name"] = ["De 2 à 40 caractères : lettres, chiffres, espaces, apostrophes et tirets."];
        if (summary.Length is < 5 or > 200)
            errors["summary"] = ["Une phrase de 5 à 200 caractères."];
        var known = WorkspaceTools.Catalog.Select(t => t.Name).ToHashSet();
        if (tools.Count == 0 || tools.Any(t => !known.Contains(t)))
            errors["tools"] = [$"Au moins un outil, parmi : {string.Join(", ", known)}."];
        return errors;
    }

    /// <summary>« Testeur de régression » donne « testeur-de-regression » : sans accents, en minuscules, 40 caractères au plus.</summary>
    internal static string KeyFrom(string name)
    {
        var flat = new string(name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        var key = string.Join('-', flat.Split('-', StringSplitOptions.RemoveEmptyEntries));
        key = key.Length > 40 ? key[..40].TrimEnd('-') : key;
        return key.Length == 0 ? "agent" : key;
    }

    private static List<ToolInfoDto> ToolsOf(AgentRole role) =>
        [.. WorkspaceTools.Catalog.Where(t => role.Tools.Contains(t.Name)).Select(t => new ToolInfoDto(t.Name, t.Description, t.RequiresApproval))];

    /// <summary>Lit les événements « usage:rôle:entrée:sortie » (leçon 52) et en fait des statistiques par rôle.</summary>
    internal static Dictionary<string, AgentStatsDto> Stats(IEnumerable<string> usageEvents)
    {
        var totals = new Dictionary<string, (int Runs, long Tokens)>();
        foreach (var type in usageEvents)
        {
            var parts = type.Split(':');
            if (parts.Length != 4 || !long.TryParse(parts[2], out var input) || !long.TryParse(parts[3], out var output))
                continue;
            var (runs, tokens) = totals.GetValueOrDefault(parts[1]);
            totals[parts[1]] = (runs + 1, tokens + input + output);
        }
        return totals.ToDictionary(t => t.Key, t => new AgentStatsDto(t.Value.Runs, t.Value.Tokens, t.Value.Tokens / t.Value.Runs));
    }

    public static IEndpointRouteBuilder MapAgentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AgentsProtocol.Path, async (
            IOptions<AgentsOptions> options, ILlmClientProvider llm, ILlmAgentModels models, AuditLog audit, AgentDefinitions definitions,
            AgentCatalog catalog, IInstanceSettings instance, HttpContext http, bool? all, CancellationToken cancellationToken) =>
        {
            // Les consignes (qui peuvent contenir des règles internes) et leur version : administrateurs seulement,
            // comme l'historique. Les autres voient le rôle, son modèle et ses outils ; et la consigne de leurs propres agents.
            // ?all=true (espace admin) : aussi les agents de chaque utilisateur.
            var isAdmin = http.User.IsInRole(Authentication.LlmAdminRole);
            var mine = await catalog.MineAsync(cancellationToken);
            var stats = Stats(await audit.TypesStartingWithAsync("usage:", cancellationToken));
            var instanceMax = (await instance.GetAsync(cancellationToken)).MaxIterations ?? options.Value.MaxIterations;
            var agents = new List<AgentDto>();
            foreach (var role in await catalog.RolesAsync(cancellationToken, everyone: isAdmin && all == true))
            {
                var admin = isAdmin || mine.GetValueOrDefault(role.Key);
                var current = await definitions.CurrentAsync(role.Key, cancellationToken);
                agents.Add(new(role.Name, role.Summary, await llm.GetModelAsync(role.Key, cancellationToken), current?.MaxIterations ?? instanceMax,
                [
                    .. WorkspaceTools.Catalog.Where(t => role.Tools.Contains(t.Name))
                        .Select(t => new ToolInfoDto(t.Name, t.Description, t.RequiresApproval)),
                ], role.Key, await models.GetAsync(role.Key, cancellationToken), stats.GetValueOrDefault(role.Key, new AgentStatsDto(0, 0, 0)),
                admin ? current?.Instructions ?? role.Instructions : "", admin ? role.Instructions : "", admin ? AgentRoles.Guardrails(role) : "",
                admin ? current?.Version ?? 0 : 0, admin ? current?.MaxIterations : null, role.Builtin, mine.ContainsKey(role.Key)));
            }
            return TypedResults.Ok(agents);
        });

        // Modifier la consigne ou la limite de tours d'un rôle (ADR 0011) : une nouvelle version, jamais un écrasement.
        app.MapPut("/agents/{role}", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            string role, UpdateAgentRequest request, AgentDefinitions definitions, AgentCatalog catalog, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (await catalog.FindAsync(role, cancellationToken) is not { } agentRole)
                return TypedResults.NotFound();
            if (!await catalog.CanEditAsync(role, cancellationToken))
                return TypedResults.Forbid();
            var instructions = request.Instructions?.Trim();
            var errors = new Dictionary<string, string[]>();
            if (instructions is not null && instructions.Length is < 20 or > 8000)
                errors["instructions"] = ["La consigne fait entre 20 et 8 000 caractères."];
            if (request.MaxIterations is not null and not (>= 1 and <= 50))
                errors["maxIterations"] = ["Entre 1 et 50 tours, ou la limite de l'instance."];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            // Revenir exactement au texte d'origine, c'est revenir à l'origine : on enregistre null.
            if (instructions == agentRole.Instructions)
                instructions = null;
            await definitions.SaveAsync(role, instructions, request.MaxIterations, http.User.Identity?.Name ?? "?", cancellationToken);
            return TypedResults.NoContent();
        });

        app.MapGet("/agents/{role}/versions", async Task<Results<Ok<AgentVersionDto[]>, NotFound>> (
            string role, AgentDefinitions definitions, AgentCatalog catalog, CancellationToken cancellationToken) =>
            await catalog.FindAsync(role, cancellationToken) is not null
                ? TypedResults.Ok((await definitions.HistoryAsync(role, cancellationToken))
                    .Select(v => new AgentVersionDto(v.Version, v.Instructions, v.MaxIterations, v.CreatedAt, v.CreatedBy)).ToArray())
                : TypedResults.NotFound())
            .RequireAuthorization(Authentication.LlmAdminPolicy);

        // Choisir le modèle d'un rôle : llm-admin, ou le propriétaire d'un agent à soi (parmi les connexions de l'instance).
        app.MapPut("/agents/{role}/model", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            string role, UpdateAgentModelRequest request, ILlmAgentModels models, AgentCatalog catalog, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (await catalog.FindAsync(role, cancellationToken) is null)
                return TypedResults.NotFound();
            if (!await catalog.CanEditAsync(role, cancellationToken))
                return TypedResults.Forbid();
            return await models.SetAsync(role, request.Model, http.User.Identity?.Name ?? "?", cancellationToken) is { } error
                ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["model"] = [error] })
                : TypedResults.NoContent();
        });

        // Ajouter un agent (ADR 0013) : outils pris dans la liste existante ; l'écriture garde l'accord humain (WorkspaceTools).
        // Tout utilisateur peut créer les siens (depuis « Nouveau collègue ») : ils ne sont visibles que de lui.
        app.MapPost(AgentsProtocol.Path, async Task<Results<Created<AgentDto>, ValidationProblem>> (
            CreateAgentRequest request, AgentsDb db, AgentCatalog catalog, AgentDefinitions definitions, ILlmClientProvider llm,
            ILlmAgentModels models, ICurrentUser me, HttpContext http, CancellationToken cancellationToken) =>
        {
            var (name, summary, tools) = Clean(request.Name, request.Summary, request.Tools);
            var instructions = (request.Instructions ?? "").Trim();
            // Une consigne vide reprend celle du poste de départ (sa version en vigueur), lue côté serveur. Seulement un poste que l'on voit ;
            // si l'on ne peut pas lire sa consigne, la copie reste cachée (InstructionsHidden) : elle sert aux missions, sans fuiter.
            var hidden = false;
            if (instructions.Length == 0 && request.BasedOn is { } basedOn)
            {
                if ((await catalog.RolesAsync(cancellationToken)).FirstOrDefault(r => r.Key == basedOn) is not { } baseRole)
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["basedOn"] = ["Poste inconnu."] });
                instructions = (await definitions.CurrentAsync(basedOn, cancellationToken))?.Instructions ?? baseRole.Instructions;
                // Le caché se transmet : une copie d'une copie cachée reste cachée.
                hidden = !await catalog.CanReadInstructionsAsync(basedOn, cancellationToken);
            }
            var errors = Validate(name, summary, tools);
            if (instructions.Length is < 20 or > 8000)
                errors["instructions"] = ["La consigne fait entre 20 et 8 000 caractères."];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            // L'identifiant vient du nom et ne change plus ; un nom déjà pris (même retiré) reçoit un suffixe.
            // Tous les agents, même ceux des autres utilisateurs : l'identifiant est unique dans toute l'instance.
            var taken = (await catalog.RolesAsync(cancellationToken, includeRetired: true, everyone: true)).Select(r => r.Key).ToHashSet();
            var key = KeyFrom(name);
            for (var n = 2; taken.Contains(key); n++)
                key = $"{KeyFrom(name)[..Math.Min(36, KeyFrom(name).Length)]}-{n}";
            var personal = request.Personal || !me.IsInstanceAdmin;
            var role = new CustomRole
            {
                Key = key, Name = name, Summary = summary, Instructions = instructions, Tools = string.Join(',', tools),
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = http.User.Identity?.Name ?? "?", OwnerId = personal ? me.Id : null,
                InstructionsHidden = hidden,
            };
            db.CustomRoles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
            // Son modèle : refusé (connexion inconnue, modèle absent), l'agent n'est pas créé.
            if (request.Model is { } model && await models.SetAsync(key, model, me.Username, cancellationToken) is { } error)
            {
                db.CustomRoles.Remove(role);
                await db.SaveChangesAsync(cancellationToken);
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["model"] = [error] });
            }
            var r = AgentCatalog.ToRole(role);
            var shown = hidden ? "" : r.Instructions;
            return TypedResults.Created(AgentsProtocol.Role(key), new AgentDto(
                r.Name, r.Summary, await llm.GetModelAsync(key, cancellationToken), 0, ToolsOf(r), key, await models.GetAsync(key, cancellationToken),
                Instructions: shown, DefaultInstructions: shown, Guardrails: AgentRoles.Guardrails(r), Builtin: false, Mine: personal));
        });

        app.MapPut("/agents/{role}/profile", async Task<Results<NoContent, NotFound, ForbidHttpResult, ValidationProblem>> (
            string role, UpdateAgentProfileRequest request, AgentsDb db, AgentCatalog catalog, CancellationToken cancellationToken) =>
        {
            // L'Analyste et le Rédacteur gardent nom et outils du code : seule leur consigne se modifie (ADR 0011).
            if (await db.CustomRoles.FirstOrDefaultAsync(r => r.Key == role && r.Active, cancellationToken) is not { } custom)
                return TypedResults.NotFound();
            if (!await catalog.CanEditAsync(role, cancellationToken))
                return TypedResults.Forbid();
            var (name, summary, tools) = Clean(request.Name, request.Summary, request.Tools);
            var errors = Validate(name, summary, tools);
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);
            custom.Name = name;
            custom.Summary = summary;
            custom.Tools = string.Join(',', tools);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.NoContent();
        });

        // Retirer : l'agent n'est plus proposé ; ses missions passées gardent son nom, une mission en pause peut finir.
        app.MapDelete("/agents/{role}", async Task<Results<NoContent, NotFound, ForbidHttpResult>> (
            string role, AgentsDb db, AgentCatalog catalog, CancellationToken cancellationToken) =>
        {
            if (await db.CustomRoles.FirstOrDefaultAsync(r => r.Key == role && r.Active, cancellationToken) is not { } custom)
                return TypedResults.NotFound();
            if (!await catalog.CanEditAsync(role, cancellationToken))
                return TypedResults.Forbid();
            custom.Active = false;
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.NoContent();
        });

        app.MapGet(AgentsProtocol.Tools, () => TypedResults.Ok(
            WorkspaceTools.Catalog.Select(t => new ToolInfoDto(t.Name, t.Description, t.RequiresApproval)).ToList()));

        SkillsEndpoints.Map(app);
        return app;
    }
}
