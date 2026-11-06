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
    Task<AgentOutcome> RunAsync(Guid missionId, string goal, MissionKind kind, AgentResume? resume, string? workspace, CancellationToken cancellationToken);
}

/// <summary>
/// Soit l'agent a fini (<see cref="Answer"/>), soit il attend une décision humaine (<see cref="Approval"/>).
/// Dans ce cas, <see cref="Conversation"/> est l'état à conserver tel quel pour reprendre plus tard.
/// </summary>
public sealed record AgentOutcome(string? Answer, AgentApproval? Approval = null, string? Conversation = null, AgentUsage? Usage = null);

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
    public static IReadOnlyList<string> RolesFor(MissionKind kind) =>
        kind == MissionKind.Review ? [AgentRoles.Analyst.Key, AgentRoles.Writer.Key] : [AgentRoles.Analyst.Key];

    public static IServiceCollection AddAgentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AgentsOptions>().BindConfiguration(AgentsOptions.Section);
        // Le modèle et sa clé viennent du module Llm (choisis dans l'application) ; voir ILlmClientProvider.
        return services
            .AddScoped<IAgentRunner, AgentFrameworkRunner>()
            .AddScoped<AgentDefinitions>()
            .AddModuleDb<AgentsDb>(AgentsDb.Schema);
    }

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
            IInstanceSettings instance, HttpContext http, CancellationToken cancellationToken) =>
        {
            // Les consignes (qui peuvent contenir des règles internes) et leur version : administrateurs seulement,
            // comme l'historique. Les autres voient le rôle, son modèle et ses outils.
            var admin = http.User.IsInRole(Authentication.LlmAdminRole);
            var stats = Stats(await audit.TypesStartingWithAsync("usage:", cancellationToken));
            var instanceMax = (await instance.GetAsync(cancellationToken)).MaxIterations ?? options.Value.MaxIterations;
            var agents = new List<AgentDto>();
            foreach (var role in AgentRoles.All)
            {
                var current = await definitions.CurrentAsync(role.Key, cancellationToken);
                agents.Add(new(role.Name, role.Summary, await llm.GetModelAsync(role.Key, cancellationToken), current?.MaxIterations ?? instanceMax,
                [
                    .. WorkspaceTools.Catalog.Where(t => role.Tools.Contains(t.Name))
                        .Select(t => new ToolInfoDto(t.Name, t.Description, t.RequiresApproval)),
                ], role.Key, await models.GetAsync(role.Key, cancellationToken), stats.GetValueOrDefault(role.Key, new AgentStatsDto(0, 0, 0)),
                admin ? current?.Instructions ?? role.Instructions : "", admin ? role.Instructions : "", admin ? AgentRoles.Guardrails(role) : "",
                admin ? current?.Version ?? 0 : 0, admin ? current?.MaxIterations : null));
            }
            return TypedResults.Ok(agents);
        });

        // Modifier la consigne ou la limite de tours d'un rôle (ADR 0011) : une nouvelle version, jamais un écrasement.
        app.MapPut("/agents/{role}", async Task<Results<NoContent, NotFound, ValidationProblem>> (
            string role, UpdateAgentRequest request, AgentDefinitions definitions, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (AgentRoles.All.FirstOrDefault(r => r.Key == role) is not { } agentRole)
                return TypedResults.NotFound();
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
        }).RequireAuthorization(Authentication.LlmAdminPolicy);

        app.MapGet("/agents/{role}/versions", async Task<Results<Ok<AgentVersionDto[]>, NotFound>> (
            string role, AgentDefinitions definitions, CancellationToken cancellationToken) =>
            AgentRoles.All.Any(r => r.Key == role)
                ? TypedResults.Ok((await definitions.HistoryAsync(role, cancellationToken))
                    .Select(v => new AgentVersionDto(v.Version, v.Instructions, v.MaxIterations, v.CreatedAt, v.CreatedBy)).ToArray())
                : TypedResults.NotFound())
            .RequireAuthorization(Authentication.LlmAdminPolicy);

        // Choisir le modèle d'un rôle : comme toute la gestion du LLM, réservé à llm-admin.
        app.MapPut("/agents/{role}/model", async Task<Results<NoContent, NotFound, ValidationProblem>> (
            string role, UpdateAgentModelRequest request, ILlmAgentModels models, HttpContext http, CancellationToken cancellationToken) =>
        {
            if (AgentRoles.All.All(r => r.Key != role))
                return TypedResults.NotFound();
            return await models.SetAsync(role, request.Model, http.User.Identity?.Name ?? "?", cancellationToken) is { } error
                ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["model"] = [error] })
                : TypedResults.NoContent();
        }).RequireAuthorization(Authentication.LlmAdminPolicy);
        return app;
    }
}
