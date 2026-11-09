using System.Net;
using System.Net.Http.Json;
using System.Text;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentYattus.Api.Tests;

/// <summary>Les fournisseurs et modèles : clés chiffrées et jamais renvoyées, catalogue, serveurs locaux, réservé à llm-admin.</summary>
public sealed class LlmTests
{
    private const string Key = "sk-ant-test-0123456789abcdefWXYZ";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<LlmConnectionDto> ConnectAsync(HttpClient client, string provider, string? key = Key, string? baseUrl = null)
    {
        var response = await client.PostAsJsonAsync(LlmProtocol.Connections, new AddLlmConnectionRequest(provider, $"Connexion {provider}", key, baseUrl), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LlmConnectionDto>(Ct))!;
    }

    [Fact]
    public async Task A_connected_key_is_stored_encrypted_and_never_sent_back()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();

        var added = await client.PostAsJsonAsync(LlmProtocol.Connections, new AddLlmConnectionRequest("anthropic", "Clé perso", Key, null), Ct);
        var status = await client.GetAsync(LlmProtocol.Path, Ct);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.DoesNotContain(Key, await added.Content.ReadAsStringAsync(Ct));
        var json = await status.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(Key, json);
        var dto = (await status.Content.ReadFromJsonAsync<LlmStatusDto>(Ct))!;
        var connection = Assert.Single(dto.Connections);
        Assert.Equal(("WXYZ", "Anthropic", "test"), (connection.Last4, connection.ProviderName, connection.CreatedBy));

        using var scope = api.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<LlmDb>().Connections.SingleAsync(Ct);
        Assert.DoesNotContain(Key, stored.ProtectedKey);
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("AgentYattus.Llm.ApiKey.v1");
        Assert.Equal(Key, protector.Unprotect(stored.ProtectedKey!));
    }

    [Fact]
    public async Task The_status_lists_every_provider_with_its_kind()
    {
        await using var api = new ApiFactory();

        var status = (await api.CreateClient().GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!;

        Assert.Contains(status.Providers, p => p is { Id: "anthropic", Kind: LlmConnectionKind.ApiKey, ModelCount: > 0 });
        Assert.Contains(status.Providers, p => p is { Id: "ollama", Kind: LlmConnectionKind.Local, DefaultBaseUrl: "http://localhost:11434/v1" });
        Assert.Contains(status.Providers, p => p is { Id: "openai-compatible", Kind: LlmConnectionKind.Custom });
        Assert.Null(status.Default);
    }

    [Fact]
    public async Task Revoking_erases_the_encrypted_key_and_clears_the_default()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var added = await ConnectAsync(client, "anthropic");
        await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(added.Id, "claude-sonnet-5-5")), Ct);

        var revoke = await client.DeleteAsync($"{LlmProtocol.Connections}/{added.Id}", Ct);
        var again = await client.DeleteAsync($"{LlmProtocol.Connections}/{added.Id}", Ct);
        var unknown = await client.DeleteAsync($"{LlmProtocol.Connections}/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var status = (await client.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!;
        Assert.Null(status.Default);
        Assert.NotNull(Assert.Single(status.Connections).RevokedAt);
        using var scope = api.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<LlmDb>().Connections.SingleAsync(Ct)).ProtectedKey);
    }

    [Fact]
    public async Task The_default_model_must_exist_for_a_live_connection()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var anthropic = await ConnectAsync(client, "anthropic");
        var ollama = await ConnectAsync(client, "ollama", key: null);

        var otherProvider = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(anthropic.Id, "gpt-4o")), Ct);
        var ok = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(anthropic.Id, "claude-sonnet-5-5")), Ct);
        var local = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(ollama.Id, "qwen3:8b")), Ct);
        var badLocalId = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(ollama.Id, "../etc passwd")), Ct);
        await client.DeleteAsync($"{LlmProtocol.Connections}/{anthropic.Id}", Ct);
        var revoked = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(anthropic.Id, "claude-sonnet-5-5")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, otherProvider.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, local.StatusCode);
        Assert.Equal(new LlmSelectionDto(ollama.Id, "qwen3:8b"), (await client.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!.Default);
        Assert.Equal(HttpStatusCode.BadRequest, badLocalId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, revoked.StatusCode);
    }

    [Theory]
    [InlineData("anthropic", "pas-une-cle", null)]
    [InlineData("anthropic", "sk-ant avec des espaces 1234567", null)]
    [InlineData("openai", null, null)] // un fournisseur en ligne exige une clé
    [InlineData("ollama", null, "http://192.168.1.20:11434/v1")] // HTTP hors de cette machine : la clé circulerait en clair
    [InlineData("openai-compatible", Key, null)] // l'adresse est obligatoire
    [InlineData("openai-compatible", Key, "ftp://llm.example.com")]
    [InlineData("openai-compatible", Key, "https://user:secret@llm.example.com/v1")]
    [InlineData("openai-compatible", Key, "https://169.254.169.254/v1")] // métadonnées d'un cloud
    [InlineData("openai-compatible", Key, "https://[fe80::1]/v1")]
    [InlineData("openai-compatible", Key, "https://[::ffff:169.254.169.254]/v1")]
    [InlineData("inconnu", Key, null)]
    public async Task Invalid_connections_are_refused(string provider, string? apiKey, string? baseUrl)
    {
        await using var api = new ApiFactory();

        var response = await api.CreateClient().PostAsJsonAsync(LlmProtocol.Connections, new AddLlmConnectionRequest(provider, "x", apiKey, baseUrl), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Local_and_custom_servers_keep_their_address_but_online_providers_cannot_be_redirected()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();

        var ollama = await ConnectAsync(client, "ollama", key: null);
        var lan = await ConnectAsync(client, "openai-compatible", baseUrl: "https://llm.example.com/v1");
        var anthropic = await ConnectAsync(client, "anthropic", baseUrl: "https://attacker.example.com");

        Assert.Equal(("http://localhost:11434/v1", null), (ollama.BaseUrl, ollama.Last4));
        Assert.Equal("https://llm.example.com/v1", lan.BaseUrl);
        // Une clé Anthropic ne part que chez Anthropic, quelle que soit l'adresse demandée.
        Assert.Null(anthropic.BaseUrl);
    }

    [Fact]
    public async Task Only_llm_admins_can_manage_providers()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "observateur");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(LlmProtocol.Path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(LlmProtocol.Models, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(LlmProtocol.Connections, new AddLlmConnectionRequest("anthropic", "x", Key, null), Ct)).StatusCode);
        // Le reste de l'application reste accessible.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/missions", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_models_come_from_the_catalog_or_from_the_local_server()
    {
        await using var api = new ApiFactory();
        var server = new FakeHttp(request => request.RequestUri!.ToString() switch
        {
            "http://localhost:11434/v1/models" => FakeHttp.Json("""{"data":[{"id":"qwen3:8b"},{"id":"llama3.1:8b"},{"id":"bad id with spaces"}]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });
        var client = api.WithFakeHttp(server).CreateClient();
        await ConnectAsync(client, "anthropic");
        var ollama = await ConnectAsync(client, "ollama", key: null);
        var lmstudio = await ConnectAsync(client, "lmstudio", key: null);

        var result = (await client.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Models, Ct))!;

        var sonnet = Assert.Single(result.Models, m => m.Id == "claude-sonnet-5-5");
        Assert.True(sonnet is { InputPricePerMillion: > 0, ContextWindow: > 0, Free: false });
        Assert.Equal(["llama3.1:8b", "qwen3:8b"], result.Models.Where(m => m.ConnectionId == ollama.Id).Select(m => m.Id).Order());
        Assert.All(result.Models.Where(m => m.ConnectionId == ollama.Id), m => Assert.True(m.Free));
        // LM Studio éteint : signalé, sans empêcher le reste de s'afficher.
        Assert.Equal(lmstudio.Id, Assert.Single(result.Errors).ConnectionId);
    }

    [Fact]
    public async Task The_agents_use_the_default_model_then_the_configuration_then_nothing()
    {
        await using var api = new ApiFactory();
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LlmDb>();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        IConfiguration Config(string? key) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = key }).Build();

        Assert.Null(await new LlmClientProvider(db, protection, Config(null), new ModelCatalog()).GetAsync("analyste", null, Ct));
        Assert.Equal("anthropic/claude-haiku-5-5",
            (await new LlmClientProvider(db, protection, Config("sk-ant-config-0123456789"), new ModelCatalog()).GetAsync("analyste", null, Ct))!.Model);

        var client = api.CreateClient();
        var groq = await ConnectAsync(client, "groq");
        var model = (await client.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Models, Ct))!.Models.First(m => m.ConnectionId == groq.Id).Id;
        await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(groq.Id, model)), Ct);
        db.ChangeTracker.Clear();

        var chosen = (await new LlmClientProvider(db, protection, Config(null), new ModelCatalog()).GetAsync("analyste", null, Ct))!;
        Assert.Equal($"groq/{model}", chosen.Model);
        // Groq parle le protocole d'OpenAI, à sa propre adresse.
        var metadata = chosen.Client.GetService<ChatClientMetadata>()!;
        Assert.Equal(("https://api.groq.com/openai/v1", model), (metadata.ProviderUri?.ToString().TrimEnd('/'), metadata.DefaultModelId));
    }

    [Fact]
    public async Task Each_agent_can_have_its_own_model_and_falls_back_to_the_default()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var anthropic = await ConnectAsync(client, "anthropic");
        var ollama = await ConnectAsync(client, "ollama", key: null);
        await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(new(anthropic.Id, "claude-sonnet-5-5")), Ct);

        var set = await client.PutAsJsonAsync(AgentsProtocol.Model("analyste"), new UpdateAgentModelRequest(new(ollama.Id, "qwen3:8b")), Ct);
        var unknownRole = await client.PutAsJsonAsync(AgentsProtocol.Model("pirate"), new UpdateAgentModelRequest(null), Ct);
        var unknownModel = await client.PutAsJsonAsync(AgentsProtocol.Model("redacteur"), new UpdateAgentModelRequest(new(anthropic.Id, "gpt-4o")), Ct);
        var agents = (await client.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, Ct))!;

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.BadRequest), (set.StatusCode, unknownRole.StatusCode, unknownModel.StatusCode));
        Assert.Equal(new LlmSelectionDto(ollama.Id, "qwen3:8b"), agents.Single(a => a.Key == "analyste").ModelOverride);
        Assert.Null(agents.Single(a => a.Key == "redacteur").ModelOverride);

        using (var scope = api.Services.CreateScope())
        {
            // ApiFactory remplace le fournisseur des agents par un faux : on teste ici le vrai.
            var real = new LlmClientProvider(scope.ServiceProvider.GetRequiredService<LlmDb>(),
                scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), new ConfigurationBuilder().Build(), scope.ServiceProvider.GetRequiredService<ModelCatalog>());
            Assert.Equal("ollama/qwen3:8b", (await real.GetAsync("analyste", null, Ct))!.Model);
            Assert.Equal("anthropic/claude-sonnet-5-5", (await real.GetAsync("redacteur", null, Ct))!.Model);
        }

        // Révoquer la connexion d'un agent le ramène au modèle par défaut.
        await client.DeleteAsync($"{LlmProtocol.Connections}/{ollama.Id}", Ct);
        var analyst = (await client.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, Ct))!.Single(a => a.Key == "analyste");
        Assert.Null(analyst.ModelOverride);
    }

    [Fact]
    public async Task Only_llm_admins_can_choose_an_agent_model()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "observateur");

        var response = await client.PutAsJsonAsync(AgentsProtocol.Model("analyste"), new UpdateAgentModelRequest(null), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_catalog_keeps_tool_calling_models_and_refreshes_from_models_dev()
    {
        var catalog = new ModelCatalog();
        Assert.NotEmpty(catalog.ModelsOf("openrouter"));
        Assert.Empty(catalog.ModelsOf("ollama"));

        var http = new HttpClient(new FakeHttp(_ => FakeHttp.Json("""
            {"anthropic":{"models":{
              "claude-new":{"name":"Claude New","tool_call":true,"reasoning":true,"limit":{"context":500000},"cost":{"input":1,"output":5}},
              "no-tools":{"name":"Sans outils","tool_call":false},
              "old":{"name":"Ancien","tool_call":true,"status":"deprecated"},
              "bad id":{"name":"x","tool_call":true}}}}
            """)));
        await catalog.RefreshAsync(http, "https://models.dev/api.json", Ct);

        var model = Assert.Single(catalog.ModelsOf("anthropic"));
        Assert.Equal(new CatalogModel("claude-new", "Claude New", 500_000, 1, 5, true), model);
    }

    [Theory]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{"anthropic": "pas un objet"}""")]
    [InlineData("""{"anthropic": {"models": [1, 2]}}""")]
    [InlineData("""{"anthropic": {"models": {"x": 42, "y": {"tool_call": true, "cost": "gratuit", "limit": []}}}}""")]
    public async Task A_malformed_catalog_never_breaks_the_current_one(string json)
    {
        var catalog = new ModelCatalog();
        var before = catalog.ModelsOf("anthropic").Count;

        await catalog.RefreshAsync(new HttpClient(new FakeHttp(_ => FakeHttp.Json(json))), "https://models.dev/api.json", Ct);

        // Rien d'utilisable : l'ancien catalogue reste ; un modèle aux champs bizarres reste lisible, sans prix.
        Assert.True(catalog.ModelsOf("anthropic").Count is var after && (after == before || after == 1));
    }

    [Theory]
    [InlineData("169.254.169.254", true)]
    [InlineData("::ffff:169.254.169.254", true)]
    [InlineData("fe80::1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.5", false)] // une passerelle d'entreprise sur le réseau privé reste permise
    public void Link_local_addresses_are_refused_even_when_disguised(string address, bool forbidden) =>
        Assert.Equal(forbidden, LlmProviders.IsForbidden(System.Net.IPAddress.Parse(address)));

    [Fact]
    public async Task The_guarded_handler_reaches_a_real_local_server()
    {
        // Un vrai petit serveur sur cette machine : la connexion passe par la vérification d'adresse après DNS.
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var server = new HttpListener();
        server.Prefixes.Add($"http://localhost:{port}/");
        server.Start();
        var serving = Task.Run(async () =>
        {
            var context = await server.GetContextAsync();
            var body = Encoding.UTF8.GetBytes("""{"data":[{"id":"qwen3:8b"}]}""");
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(body, Ct);
            context.Response.Close();
        }, Ct);

        using var http = new HttpClient(LlmProviders.CreateGuardedHandler());
        var models = await ModelCatalog.DiscoverAsync(http, $"http://localhost:{port}/v1", null, Ct);

        Assert.Equal("qwen3:8b", Assert.Single(models).Id);
        await serving;
    }

    [Fact]
    public async Task A_huge_answer_without_announced_size_is_refused_after_the_limit()
    {
        var huge = "{\"data\":[" + string.Join(",", Enumerable.Repeat("{\"id\":\"model-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"}", 30_000)) + "]}";
        var http = new HttpClient(new FakeHttp(_ =>
        {
            var response = FakeHttp.Json(huge);
            response.Content.Headers.ContentLength = null; // comme une réponse « chunked »
            return response;
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelCatalog.DiscoverAsync(http, "http://localhost:11434/v1", null, Ct));
    }

    [Fact]
    public async Task A_local_server_that_answers_too_much_is_ignored()
    {
        await using var api = new ApiFactory();
        var huge = "{\"data\":[" + string.Join(",", Enumerable.Repeat("{\"id\":\"model-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"}", 30_000)) + "]}";
        var client = api.WithFakeHttp(new FakeHttp(_ => FakeHttp.Json(huge))).CreateClient();
        var ollama = await ConnectAsync(client, "ollama", key: null);

        var result = (await client.GetFromJsonAsync<LlmModelsDto>(LlmProtocol.Models, Ct))!;

        Assert.Empty(result.Models);
        Assert.Equal(ollama.Id, Assert.Single(result.Errors).ConnectionId);
    }
}

/// <summary>Un faux serveur HTTP : aucune requête ne sort de la machine pendant les tests.</summary>
public sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

public static class FakeHttpExtensions
{
    public static WebApplicationFactory<Program> WithFakeHttp(this WebApplicationFactory<Program> api, FakeHttp handler) =>
        api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient(ModelCatalog.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler)));
}
