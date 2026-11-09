using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static AgentYattus.Api.Tests.IdentityTests;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>
/// Les clés personnelles (leçon 134) : chacun connecte les siennes ; elles ne servent qu'à ses missions, ne se voient pas des autres
/// (administrateur compris) et ne comptent pas dans le budget de l'instance.
/// </summary>
public sealed class PersonalKeysTests
{
    private const string AliceKey = "sk-ant-alice-0123456789abcdefALIC";
    private const string HouseKey = "sk-ant-house-0123456789abcdefHOUS";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<LlmConnectionDto> ConnectMineAsync(HttpClient client, string provider = "anthropic", string? key = AliceKey)
    {
        var response = await client.PostAsJsonAsync(LlmProtocol.MineConnections, new AddLlmConnectionRequest(provider, "Ma clé", key, null), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LlmConnectionDto>(Ct))!;
    }

    private static LlmClientProvider Real(ApiFactory api, IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<LlmDb>(), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
        new ConfigurationBuilder().Build(), scope.ServiceProvider.GetRequiredService<ModelCatalog>());

    [Fact]
    public async Task Personal_free_mode_uses_multiple_owned_providers_and_never_paid_or_foreign_keys()
    {
        await using var api = new ApiFactory();
        var alice = As(api, "alice");
        var bob = As(api, "bob");
        var first = await ConnectMineAsync(alice, "openrouter", "test-openrouter-key-1234567890");
        var second = await ConnectMineAsync(alice, "nvidia", "test-nvidia-key-1234567890");
        await ConnectMineAsync(bob, "nvidia", "test-bob-key-1234567890");
        var paid = await ConnectMineAsync(alice);
        await alice.PutAsJsonAsync(LlmProtocol.MineDefault, new UpdateMyDefaultRequest(new(paid.Id, "claude-sonnet-5-5")), Ct);
        Assert.Equal(HttpStatusCode.NoContent,
            (await alice.PutAsJsonAsync(LlmProtocol.MineFree, new UpdateMyFreeModeRequest(true), Ct)).StatusCode);
        Assert.True((await alice.GetFromJsonAsync<MyLlmDto>(LlmProtocol.Mine, Ct))!.FreeMode);
        Assert.False((await bob.GetFromJsonAsync<MyLlmDto>(LlmProtocol.Mine, Ct))!.FreeMode);
        Assert.Equal(3, (await alice.GetFromJsonAsync<MyLlmDto>(LlmProtocol.Mine, Ct))!.Connections.Count);
        using (var scope = api.Services.CreateScope())
        {
            var real = Real(api, scope);
            var label = await real.GetModelAsync("analyste", "alice", Ct);
            Assert.StartsWith("gratuit : openrouter/", label);
            Assert.Contains("(+", label);
            var client = (await real.GetAsync("analyste", "alice", Ct))!;
            Assert.True(client.Personal);
            Assert.IsType<FallbackChatClient>(client.Client);
        }
        // Après révocation du premier fournisseur, le second est utilisé automatiquement.
        await alice.DeleteAsync($"{LlmProtocol.MineConnections}/{first.Id}", Ct);
        using (var scope = api.Services.CreateScope())
            Assert.StartsWith("gratuit : nvidia/", await Real(api, scope).GetModelAsync("analyste", "alice", Ct));
        await alice.DeleteAsync($"{LlmProtocol.MineConnections}/{second.Id}", Ct);
        using (var scope = api.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => Real(api, scope).GetAsync("analyste", "alice", Ct));
        await alice.PutAsJsonAsync(LlmProtocol.MineFree, new UpdateMyFreeModeRequest(false), Ct);
        using (var scope = api.Services.CreateScope())
            Assert.Equal("anthropic/claude-sonnet-5-5", await Real(api, scope).GetModelAsync("analyste", "alice", Ct));
    }

    [Fact]
    public async Task Each_user_manages_their_own_keys_that_nobody_else_sees_or_revokes()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var alice = As(api, "alice");
        var bob = As(api, "bob");

        var mine = await ConnectMineAsync(alice);
        Assert.True(mine.Personal);
        Assert.Equal("ALIC", mine.Last4);
        // Un utilisateur sans rôle d'administrateur ne touche pas aux connexions de l'instance.
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync(LlmProtocol.Path, Ct)).StatusCode);

        // Alice voit la sienne, sans la clé ; Bob et l'administrateur ne la voient pas.
        var aliceView = await alice.GetStringAsync(LlmProtocol.Mine, Ct);
        Assert.Contains(mine.Id.ToString(), aliceView);
        Assert.DoesNotContain(AliceKey, aliceView);
        Assert.Empty((await bob.GetFromJsonAsync<MyLlmDto>(LlmProtocol.Mine, Ct))!.Connections);
        Assert.DoesNotContain(mine.Id.ToString(), await root.GetStringAsync(LlmProtocol.Path, Ct));
        Assert.DoesNotContain(mine.Id.ToString(), await root.GetStringAsync(LlmProtocol.Models, Ct));
        // Les modèles qu'on peut donner à ses agents : ceux de sa clé pour Alice, pas pour Bob.
        Assert.Contains((await alice.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Choices, Ct))!.Models, m => m.ConnectionId == mine.Id && m.Personal);
        Assert.DoesNotContain((await bob.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Choices, Ct))!.Models, m => m.ConnectionId == mine.Id);

        // Bob ne peut ni la révoquer, ni la prendre comme modèle par défaut ; l'administrateur ne la révoque pas non plus.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"{LlmProtocol.MineConnections}/{mine.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await root.DeleteAsync($"{LlmProtocol.Connections}/{mine.Id}", Ct)).StatusCode);
        var stolen = await bob.PutAsJsonAsync(LlmProtocol.MineDefault, new UpdateMyDefaultRequest(new(mine.Id, "claude-sonnet-5-5")), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        // Ni le défaut de l'instance, ni la chaîne Gratuite, ni le modèle d'un agent de l'instance ne passent par une clé personnelle.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await root.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(mine.Id, "claude-sonnet-5-5")), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await root.PutAsJsonAsync(AgentsProtocol.Model("analyste"), new UpdateAgentModelRequest(new(mine.Id, "claude-sonnet-5-5")), Ct)).StatusCode);

        // Un serveur local ou une adresse au choix : refusés en clé personnelle (le serveur appellerait l'adresse donnée).
        var local = await alice.PostAsJsonAsync(LlmProtocol.MineConnections, new AddLlmConnectionRequest("ollama", "Mon Ollama", null, "http://localhost:11434/v1"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, local.StatusCode);
        Assert.Contains("provider", await local.Content.ReadAsStringAsync(Ct));
        Assert.DoesNotContain((await alice.GetFromJsonAsync<MyLlmDto>(LlmProtocol.Mine, Ct))!.Providers, p => p.Kind != LlmConnectionKind.ApiKey);

        // Alice la révoque : la clé chiffrée est effacée, son modèle par défaut aussi.
        Assert.Equal(HttpStatusCode.NoContent,
            (await alice.PutAsJsonAsync(LlmProtocol.MineDefault, new UpdateMyDefaultRequest(new(mine.Id, "claude-sonnet-5-5")), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"{LlmProtocol.MineConnections}/{mine.Id}", Ct)).StatusCode);
        var after = (await alice.GetFromJsonAsync<MyLlmDto>(LlmProtocol.Mine, Ct))!;
        Assert.NotNull(Assert.Single(after.Connections).RevokedAt);
        Assert.Null(after.Default);
        using var scope = api.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<LlmDb>().Connections.FindAsync([mine.Id], Ct))!.ProtectedKey);
    }

    [Fact]
    public async Task A_mission_uses_the_key_of_whoever_launches_it_and_never_someone_elses()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var alice = As(api, "alice");
        var house = (await (await root.PostAsJsonAsync(LlmProtocol.Connections, new AddLlmConnectionRequest("anthropic", "Instance", HouseKey, null), Ct))
            .Content.ReadFromJsonAsync<LlmConnectionDto>(Ct))!;
        await root.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(house.Id, "claude-haiku-5-5")), Ct);
        var mine = await ConnectMineAsync(alice);
        await alice.PutAsJsonAsync(LlmProtocol.MineDefault, new UpdateMyDefaultRequest(new(mine.Id, "claude-sonnet-5-5")), Ct);

        using var scope = api.Services.CreateScope();
        var real = Real(api, scope);
        // Alice : sa clé, marquée personnelle. Bob (même projet partagé ou non) : celle de l'instance. Sans personne : l'instance.
        var forAlice = (await real.GetAsync("analyste", "alice", Ct))!;
        Assert.Equal(("anthropic/claude-sonnet-5-5", true), (forAlice.Model, forAlice.Personal));
        var forBob = (await real.GetAsync("analyste", "bob", Ct))!;
        Assert.Equal(("anthropic/claude-haiku-5-5", false), (forBob.Model, forBob.Personal));
        Assert.False((await real.GetAsync("analyste", null, Ct))!.Personal);
        Assert.Equal("anthropic/claude-sonnet-5-5", await real.GetModelAsync("redacteur", "alice", Ct));

        // Le mode Gratuit de l'instance ne prend jamais la clé d'Alice dans sa chaîne.
        await root.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(true, []), Ct);
        var free = await Real(api, scope).GetModelAsync("analyste", "bob", Ct);
        Assert.DoesNotContain("sonnet", free);
    }

    [Fact]
    public async Task A_personal_key_is_marked_in_the_journal_and_not_counted_in_the_instance_budget()
    {
        // Claude Haiku 5.5 au catalogue : 1 000 × 0,10 + 200 × 0,50 = 200 micro-dollars, payés par la clé personnelle.
        var model = new ScriptedChatClient(Text("Réponse.", input: 1_000, output: 200));
        await using var api = new ScriptedModelApiFactory(model, modelName: "anthropic/claude-haiku-5-5") { PersonalKey = true };
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Combien ça coûte ?");
        await client.WaitForFinalStatusAsync(mission.Id);

        var done = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", Ct))!;
        Assert.Equal(0.0002m, done.Cost);
        Assert.Contains("cle:personnelle", await api.AuditOfAsync(mission.Id));
        Assert.Equal(0m, (await client.GetFromJsonAsync<BudgetDto>(SettingsProtocol.Budget, Ct))!.Spent);
    }
}
