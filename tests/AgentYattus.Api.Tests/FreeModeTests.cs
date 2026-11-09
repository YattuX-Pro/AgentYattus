using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Le mode Gratuit : une chaîne de modèles gratuits seulement, et la bascule quand un quota est épuisé.</summary>
public sealed class FreeModeTests
{
    private const string Key = "nvapi-test-0123456789abcdefWXYZ";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Un modèle dont le quota est épuisé : il répond 429 à chaque appel.</summary>
    private sealed class ExhaustedChatClient(HttpStatusCode status = HttpStatusCode.TooManyRequests) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new HttpRequestException("quota", null, status);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task An_exhausted_model_hands_over_to_the_next_and_stays_there()
    {
        var exhausted = new ExhaustedChatClient();
        var next = new ScriptedChatClient(Text("un"), Text("deux"));
        var chain = new FallbackChatClient([new(exhausted, "openrouter/a:free"), new(next, "nvidia/b")]);

        Assert.Equal("un", (await chain.GetResponseAsync([new(ChatRole.User, "1")], cancellationToken: Ct)).Text);
        Assert.Equal("deux", (await chain.GetResponseAsync([new(ChatRole.User, "2")], cancellationToken: Ct)).Text);

        // Le modèle épuisé n'est pas réessayé à chaque tour.
        Assert.Equal(1, exhausted.Calls);
        Assert.Equal(["openrouter/a:free", "nvidia/b"], chain.Used);
    }

    [Fact]
    public async Task Other_errors_and_an_exhausted_chain_are_not_hidden()
    {
        var badRequest = new FallbackChatClient([new(new ExhaustedChatClient(HttpStatusCode.BadRequest), "a"), new(new ScriptedChatClient(Text("x")), "b")]);
        var allExhausted = new FallbackChatClient([new(new ExhaustedChatClient(), "a"), new(new ExhaustedChatClient(), "b")]);

        // Une requête refusée (400) n'est pas un quota : on ne la cache pas derrière un autre modèle.
        await Assert.ThrowsAsync<HttpRequestException>(() => badRequest.GetResponseAsync([new(ChatRole.User, "?")], cancellationToken: Ct));
        await Assert.ThrowsAsync<HttpRequestException>(() => allExhausted.GetResponseAsync([new(ChatRole.User, "?")], cancellationToken: Ct));
        Assert.Equal(["a", "b"], allExhausted.Used);
    }

    [Fact]
    public async Task A_switch_is_written_in_the_mission_journal()
    {
        var chain = new FallbackChatClient([new(new ExhaustedChatClient(), "openrouter/a:free"), new(new ScriptedChatClient(Text("Réponse.")), "nvidia/b")]);
        await using var api = new ScriptedModelApiFactory(chain);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Bonjour ?");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Contains("bascule:nvidia/b", await api.AuditOfAsync(mission.Id));
    }

    [Fact]
    public async Task The_chain_accepts_only_free_models()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var nvidia = await ConnectAsync(client, "nvidia", Key);
        var anthropic = await ConnectAsync(client, "anthropic", Key);
        var ollama = await ConnectAsync(client, "ollama", null);
        var free = (await client.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Models, Ct))!.Models.First(m => m.ConnectionId == nvidia.Id && m.Free);

        var paid = await client.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(true, [new(anthropic.Id, "claude-sonnet-5-5")]), Ct);
        var automatic = await client.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(true, []), Ct);
        var autoStatus = (await client.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!;
        Assert.True(autoStatus.FreeMode);
        Assert.NotEmpty(autoStatus.FreeChain!);
        Assert.All(autoStatus.FreeChain!, link => Assert.Equal(nvidia.Id, link.ConnectionId));
        var ok = await client.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(true, [new(nvidia.Id, free.Id), new(ollama.Id, "qwen3:8b")]), Ct);

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.NoContent, HttpStatusCode.NoContent), (paid.StatusCode, automatic.StatusCode, ok.StatusCode));
        var status = (await client.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!;
        Assert.True(status.FreeMode);
        Assert.Equal([new(nvidia.Id, free.Id), new LlmSelectionDto(ollama.Id, "qwen3:8b")], status.FreeChain);
        Assert.Contains(status.Providers, p => p.Id == "openrouter" && p.Notice is { Length: > 0 });

        // Révoquer une connexion la retire de la chaîne.
        await client.DeleteAsync($"{LlmProtocol.Connections}/{nvidia.Id}", Ct);
        Assert.Equal([new LlmSelectionDto(ollama.Id, "qwen3:8b")], (await client.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!.FreeChain);
    }

    [Fact]
    public async Task Free_mode_overrides_agent_models_and_disabling_restores_them()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var ollama = await ConnectAsync(client, "ollama", null);
        var anthropic = await ConnectAsync(client, "anthropic", Key);
        await client.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(true, [new(ollama.Id, "qwen3:8b"), new(ollama.Id, "llama3.1:8b")]), Ct);
        await client.PutAsJsonAsync(AgentsProtocol.Model("redacteur"), new UpdateAgentModelRequest(new(anthropic.Id, "claude-sonnet-5-5")), Ct);

        using var scope = api.Services.CreateScope();
        var real = new LlmClientProvider(scope.ServiceProvider.GetRequiredService<LlmDb>(),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), new ConfigurationBuilder().Build(), scope.ServiceProvider.GetRequiredService<ModelCatalog>());

        var analyst = (await real.GetAsync("analyste", null, Ct))!;
        Assert.NotNull(analyst.Client.GetService<FallbackChatClient>());
        Assert.Equal("gratuit : ollama/qwen3:8b (+1)", await real.GetModelAsync("analyste", null, Ct));
        Assert.NotNull((await real.GetAsync("redacteur", null, Ct))!.Client.GetService<FallbackChatClient>());
        Assert.Equal("gratuit : ollama/qwen3:8b (+1)", await real.GetModelAsync("redacteur", null, Ct));
        await client.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(false, []), Ct);
        scope.ServiceProvider.GetRequiredService<LlmDb>().ChangeTracker.Clear();
        Assert.Equal("anthropic/claude-sonnet-5-5", (await real.GetAsync("redacteur", null, Ct))!.Model);
    }

    [Fact]
    public async Task Free_mode_discovers_backups_even_with_only_one_selected_model()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var connection = await ConnectAsync(client, "openrouter", Key);
        var models = (await client.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Models, Ct))!.Models
            .Where(m => m.ConnectionId == connection.Id && m.Free).ToList();
        Assert.True(models.Count > 1);
        await client.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(true, [new(connection.Id, models[0].Id)]), Ct);
        using var scope = api.Services.CreateScope();
        var real = new LlmClientProvider(scope.ServiceProvider.GetRequiredService<LlmDb>(),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), new ConfigurationBuilder().Build(),
            scope.ServiceProvider.GetRequiredService<ModelCatalog>());
        Assert.Contains("(+", await real.GetModelAsync("analyste", null, Ct));
        Assert.NotNull((await real.GetAsync("analyste", null, Ct))!.Client.GetService<FallbackChatClient>());
    }

    [Fact]
    public async Task Revoking_all_free_connections_never_falls_back_to_a_paid_configuration()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var connection = await ConnectAsync(client, "ollama", null);
        await client.PutAsJsonAsync(LlmProtocol.Free, new UpdateFreeModeRequest(true, [new(connection.Id, "qwen3:8b")]), Ct);
        await client.DeleteAsync($"{LlmProtocol.Connections}/{connection.Id}", Ct);
        using var scope = api.Services.CreateScope();
        var real = new LlmClientProvider(scope.ServiceProvider.GetRequiredService<LlmDb>(),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Anthropic:ApiKey"] = Key }).Build(), scope.ServiceProvider.GetRequiredService<ModelCatalog>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => real.GetAsync("analyste", null, Ct));
        Assert.Equal("gratuit : aucun modèle disponible", await real.GetModelAsync("analyste", null, Ct));
    }

    private static async Task<LlmConnectionDto> ConnectAsync(HttpClient client, string provider, string? key)
    {
        var response = await client.PostAsJsonAsync(LlmProtocol.Connections, new AddLlmConnectionRequest(provider, provider, key, null), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LlmConnectionDto>(Ct))!;
    }
}
