using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Missions;
using AgentYattus.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

public sealed class BudgetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task SetBudgetAsync(HttpClient client, decimal? budget)
    {
        var settings = (await client.GetFromJsonAsync<InstanceSettingsDto>(SettingsProtocol.Path, Ct))!;
        await client.PutAsJsonAsync(SettingsProtocol.Path, SettingsTests.Request(settings) with { MonthlyBudget = budget }, Ct);
    }

    [Fact]
    public async Task At_the_ceiling_paid_missions_are_refused_before_calling_the_model()
    {
        // Une mission à 1 million de tokens d'entrée sur Haiku : 0,10 $.
        var model = new ScriptedChatClient(Text("Réponse chère.", input: 1_000_000));
        await using var api = new ScriptedModelApiFactory(model, modelName: "anthropic/claude-haiku-5-5");
        var client = api.CreateClient();
        await SetBudgetAsync(client, 0.05m);

        var first = await client.CreateMissionAsync("Première");
        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(first.Id));
        var budget = (await client.GetFromJsonAsync<BudgetDto>(SettingsProtocol.Budget, Ct))!;
        Assert.Equal((0.10m, true, true), (budget.Spent, budget.Alert, budget.Reached));

        // Plafond dépassé : la seconde échoue sans appeler le modèle (son script est vide, l'appeler lèverait une erreur).
        var second = await client.CreateMissionAsync("Seconde");
        Assert.Equal(MissionStatus.Failed, await client.WaitForFinalStatusAsync(second.Id));
        Assert.Contains("Budget du mois atteint", (await client.GetFromJsonAsync<MissionDto>($"/missions/{second.Id}", Ct))!.Result);
        Assert.Contains("budget:atteint", await api.AuditOfAsync(second.Id));
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task Free_models_still_run_at_the_ceiling_and_only_admins_see_the_budget()
    {
        var model = new ScriptedChatClient(Text("Gratuit."));
        await using var api = new ScriptedModelApiFactory(model, modelName: "ollama/qwen3:8b");
        var client = api.CreateClient();
        await SetBudgetAsync(client, 0.01m);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MissionsDb>();
            db.Missions.Add(new Mission { Id = Guid.CreateVersion7(), Goal = "Déjà payée", Status = MissionStatus.Completed, CreatedAt = DateTimeOffset.UtcNow, CostMicros = 50_000 });
            await db.SaveChangesAsync(Ct);
        }

        var mission = await client.CreateMissionAsync("Sur un modèle local");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        var observer = api.CreateClient();
        observer.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "membre");
        Assert.Equal(HttpStatusCode.Forbidden, (await observer.GetAsync(SettingsProtocol.Budget, Ct)).StatusCode);
    }
}
