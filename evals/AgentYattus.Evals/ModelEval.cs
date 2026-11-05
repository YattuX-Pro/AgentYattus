using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using AgentYattus.Api;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Missions;
using AgentYattus.Contracts;
using Anthropic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentYattus.Evals;

/// <summary>
/// L'évaluation sur de vrais modèles : chaque cas, pour chaque modèle, dans l'API complète.
/// PAYANTE : ne tourne que si AGENTYATTUS_EVAL_MODELS est défini, par exemple « claude-haiku-5-5,claude-sonnet-5-5 ».
/// </summary>
public sealed class ModelEval(ITestOutputHelper output)
{
    [Fact]
    public async Task Cost_per_successful_mission_by_model()
    {
        var models = Environment.GetEnvironmentVariable("AGENTYATTUS_EVAL_MODELS")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        Assert.SkipWhen(models.Length == 0, "Évaluation payante : définir AGENTYATTUS_EVAL_MODELS pour la lancer.");
        var apiKey = new ConfigurationBuilder().AddUserSecrets<ModelEval>().AddEnvironmentVariables().Build()["Anthropic:ApiKey"];
        Assert.SkipWhen(string.IsNullOrWhiteSpace(apiKey), "Clé absente : dotnet user-secrets set \"Anthropic:ApiKey\" … --project src/AgentYattus.Api");

        var cases = EvalCase.Load();
        output.WriteLine($"| Modèle | Cas | Réussi | Coût | Raison |");
        output.WriteLine($"|---|---|---|---|---|");
        foreach (var model in models)
        {
            Assert.True(Pricing.Knows(model), $"Prix inconnu pour {model} : compléter Pricing.");
            decimal total = 0;
            var successes = 0;
            foreach (var evalCase in cases)
            {
                using var chatClient = new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model, defaultMaxOutputTokens: 16_000);
                var (verdict, cost) = await RunAsync(model, chatClient, evalCase);
                total += cost;
                successes += verdict.Success ? 1 : 0;
                output.WriteLine($"| {model} | {evalCase.Name} | {(verdict.Success ? "oui" : "non")} | {cost:0.0000} $ | {verdict.Reason} |");
            }

            var perSuccess = successes == 0 ? "—" : $"{total / successes:0.0000} $";
            output.WriteLine($"| **{model}** | **{successes}/{cases.Count}** | | **{total:0.0000} $** | **par mission réussie : {perSuccess}** |");
        }
    }

    internal static async Task<(EvalVerdict Verdict, decimal Cost)> RunAsync(string model, IChatClient chatClient, EvalCase evalCase)
    {
        // Chaque cas part d'une copie neuve du workspace : un REPORT.md écrit par un cas ne fausse pas le suivant.
        var workspace = Directory.CreateTempSubdirectory("yattus-eval-").FullName;
        CopyDirectory(Path.Combine(FindRepoRoot(), "evals", "workspaces", "orders-api"), workspace);

        // Les tokens viennent des traces OpenTelemetry de chaque appel au modèle (leçon 21).
        var calls = new ConcurrentQueue<(long Input, long Output)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.DisplayName.StartsWith("chat", StringComparison.Ordinal))
                    calls.Enqueue((Convert.ToInt64(activity.GetTagItem("gen_ai.usage.input_tokens") ?? 0L),
                                   Convert.ToInt64(activity.GetTagItem("gen_ai.usage.output_tokens") ?? 0L)));
            },
        };
        ActivitySource.AddActivityListener(listener);

        await using var api = new EvalApi(chatClient, model, workspace);
        var client = api.CreateClient();
        var created = await (await client.PostAsJsonAsync("/missions", new CreateMissionRequest(evalCase.Goal, evalCase.Kind)))
            .Content.ReadFromJsonAsync<MissionDto>();

        var mission = await WaitAsync(client, created!.Id);
        // L'évaluation joue le rôle de l'humain : elle approuve chaque écriture demandée, pour noter le rapport.
        while (mission.Status == MissionStatus.AwaitingApproval)
        {
            await client.PostAsJsonAsync($"/missions/{mission.Id}/decision", new DecisionRequest(true, mission.PendingApproval!.Id));
            mission = await WaitAsync(client, mission.Id, skip: MissionStatus.AwaitingApproval);
        }

        var reportPath = Path.Combine(workspace, "REPORT.md");
        var report = File.Exists(reportPath) ? await File.ReadAllTextAsync(reportPath) : null;
        Directory.Delete(workspace, recursive: true);
        return (evalCase.Grade(mission.Status, mission.Result, report), calls.Sum(c => Pricing.Cost(model, c.Input, c.Output)));
    }

    // Attend un état stable (fini, ou en attente d'une nouvelle décision) ; 5 minutes au plus par étape.
    private static async Task<MissionDto> WaitAsync(HttpClient client, Guid id, MissionStatus? skip = null)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (true)
        {
            var mission = (await client.GetFromJsonAsync<MissionDto>($"/missions/{id}"))!;
            if (mission.Status is MissionStatus.Completed or MissionStatus.Failed
                || (mission.Status == MissionStatus.AwaitingApproval && skip != MissionStatus.AwaitingApproval))
                return mission;
            if (mission.Status == MissionStatus.AwaitingApproval && skip == MissionStatus.AwaitingApproval)
                skip = null; // nouvelle pause après la reprise : on la rend
            if (DateTime.UtcNow > deadline)
                return mission;
            await Task.Delay(500);
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AgentYattus.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Racine du dépôt introuvable.");
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}

/// <summary>L'API en mémoire, sur SQLite, branchée sur un vrai modèle et un workspace d'évaluation.</summary>
internal sealed class EvalApi(IChatClient chatClient, string model, string workspace) : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"agentyattus-eval-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            UseSqlite<MissionsDb>(services);
            UseSqlite<AuditDb>(services);
            services.RemoveAll<IChatClient>();
            services.AddSingleton(chatClient);
            services.Configure<AgentsOptions>(options =>
            {
                options.Workspace = workspace;
                options.Model = model;
            });
        });

    private void UseSqlite<TDb>(IServiceCollection services) where TDb : DbContext
    {
        services.RemoveAll<IDbContextOptionsConfiguration<TDb>>();
        services.AddDbContext<TDb>(o => o.UseSqlite($"Data Source={_dbPath}"));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
