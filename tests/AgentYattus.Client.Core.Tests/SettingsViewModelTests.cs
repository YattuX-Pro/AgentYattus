using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Server_free_mode_is_accessible_to_an_administrator_without_replacing_personal_settings()
    {
        var calls = 0;
        var api = new ApiClient(new HttpClient(new Stub(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        })) { BaseAddress = new Uri("http://api/") });
        var oidc = new OidcClient(new HttpClient(), new OidcSettings(new Uri("http://auth/"), "desktop"));
        var login = new LoginViewModel(oidc, new TokenStore(oidc), _ => Task.CompletedTask);
        var shell = new ShellViewModel(api, new HomeViewModel(api), login);
        await shell.ShowInstanceModelsCommand.ExecuteAsync(null);
        Assert.Same(login, shell.Current);
        Assert.Equal(0, calls);
        shell.IsLlmAdmin = true;
        await shell.ShowInstanceModelsCommand.ExecuteAsync(null);
        Assert.True(Assert.IsType<SettingsViewModel>(shell.Current).IsInstance);
        await shell.ShowSettingsCommand.ExecuteAsync(null);
        Assert.True(Assert.IsType<SettingsViewModel>(shell.Current).IsPersonal);
    }

    private static readonly Guid AnthropicId = Guid.NewGuid();
    private static readonly Guid OllamaId = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LlmProviderDto[] Providers =
    [
        new("anthropic", "Anthropic", LlmConnectionKind.ApiKey, null, 3),
        new("ollama", "Ollama", LlmConnectionKind.Local, "http://localhost:11434/v1", 0),
    ];

    private static LlmStatusDto Status(bool revoked = false) => new(Providers,
        [
            new LlmConnectionDto(AnthropicId, "anthropic", "Anthropic", "Clé perso", "WXYZ", null, DateTimeOffset.UtcNow, "dev", revoked ? DateTimeOffset.UtcNow : null),
            new LlmConnectionDto(OllamaId, "ollama", "Ollama", "Ollama", null, "http://localhost:11434/v1", DateTimeOffset.UtcNow, "dev", null),
        ],
        revoked ? null : new LlmSelectionDto(AnthropicId, "claude-haiku-5-5"));

    private static LlmModelsDto Models() => new(
        [
            new LlmModelDto(AnthropicId, "anthropic", "claude-haiku-5-5", "Claude Haiku 5.5", 200_000, 0.10m, 0.50m, true, false),
            new LlmModelDto(AnthropicId, "anthropic", "claude-sonnet-5-5", "Claude Sonnet 5.5", 1_000_000, 2m, 10m, true, false),
            new LlmModelDto(OllamaId, "ollama", "qwen3:8b", "qwen3:8b", null, 0, 0, false, true),
        ], []);

    private static AgentDto[] Agents() =>
    [
        new("Analyste", "Lit", "ollama/qwen3:8b", 10, [], "analyste", new LlmSelectionDto(OllamaId, "qwen3:8b")),
        new("Rédacteur", "Écrit", "anthropic/claude-haiku-5-5", 10, [], "redacteur"),
    ];

    private static (SettingsViewModel Vm, List<(HttpMethod Method, string Path, string? Body)> Calls) Create(Func<HttpRequestMessage, HttpResponseMessage?>? respond = null)
    {
        var calls = new List<(HttpMethod, string, string?)>();
        var revoked = false;
        var handler = new Stub(request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            if (respond?.Invoke(request) is { } custom)
                return custom;
            if (request.Method == HttpMethod.Delete)
                revoked = true;
            if (request.Method != HttpMethod.Get)
                return new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath switch
                {
                    LlmProtocol.Models => JsonContent.Create(Models()),
                    AgentsProtocol.Path => JsonContent.Create(Agents()),
                    _ => JsonContent.Create(Status(revoked)),
                },
            };
        });
        return (new SettingsViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") })), calls);
    }

    [Fact]
    public async Task My_keys_only_talk_to_the_personal_routes_and_hide_the_instance_settings()
    {
        var calls = new List<(HttpMethod Method, string Path, string? Body)>();
        LlmSelectionDto? myDefault = new(AnthropicId, "claude-haiku-5-5");
        var freeMode = false;
        var handler = new Stub(request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            if (request.RequestUri.AbsolutePath == LlmProtocol.MineFree && request.Method == HttpMethod.Put)
                freeMode = JsonSerializer.Deserialize<UpdateMyFreeModeRequest>(body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Enabled;
            if (request.Method == HttpMethod.Put)
                myDefault = body!.Contains("\"default\":null") ? null : myDefault;
            if (request.Method != HttpMethod.Get)
                return new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
            object content = request.RequestUri.AbsolutePath == LlmProtocol.MineModels
                ? new LlmModelsDto([new LlmModelDto(AnthropicId, "anthropic", "claude-haiku-5-5", "Claude Haiku 5.5", 200_000, 0.10m, 0.50m, true, false, Personal: true)], [])
                : new MyLlmDto([Providers[0]],
                    [new LlmConnectionDto(AnthropicId, "anthropic", "Anthropic", "Ma clé", "WXYZ", null, DateTimeOffset.UtcNow, "alice", null, Personal: true)], myDefault, freeMode);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content, content.GetType()) };
        });
        var vm = new SettingsViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }), personal: true);

        await vm.LoadAsync(Ct);

        // Mes clés : le titre et le modèle personnel ; pas de modèle par agent de l’instance.
        Assert.Equal(("Mes clés", "Claude Haiku 5.5", false), (vm.Heading, vm.DefaultLabel, vm.IsInstance));
        Assert.Equal("anthropic", Assert.Single(vm.Providers).Id);
        Assert.Empty(vm.Agents);

        await vm.ChooseCommand.ExecuteAsync(vm.FilteredModels[0]);
        vm.PickCommand.Execute(vm.Providers[0]);
        vm.NewKey = "sk-ant-alice-0123456789abcdef";
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.False(vm.IsPickerOpen);
        vm.FreeMode = true;
        Assert.True(freeMode);
        Assert.Contains(calls, c => c.Method == HttpMethod.Put && c.Path == LlmProtocol.MineFree);
        vm.AskRevokeCommand.Execute(vm.Connections[0]);
        await vm.ConfirmRevokeCommand.ExecuteAsync(null);
        Assert.True(vm.CanUseInstanceModel);
        Assert.Equal("Sélection automatique · modèles gratuits", vm.DefaultLabel);
        vm.FreeMode = false;
        await vm.UseInstanceModelCommand.ExecuteAsync(null);
        Assert.Equal("Celui de l'instance", vm.DefaultLabel);

        // Jamais une route de l'instance ; seulement /llm/mine.
        Assert.All(calls, c => Assert.StartsWith(LlmProtocol.Mine, c.Path));
        Assert.Contains(calls, c => c.Method == HttpMethod.Put && c.Path == LlmProtocol.MineDefault && c.Body!.Contains("claude-haiku-5-5"));
        Assert.Contains(calls, c => c.Method == HttpMethod.Post && c.Path == LlmProtocol.MineConnections);
        Assert.Contains(calls, c => c.Method == HttpMethod.Delete && c.Path == $"{LlmProtocol.MineConnections}/{AnthropicId}");
        Assert.Contains(calls, c => c.Method == HttpMethod.Put && c.Body!.Contains("\"default\":null"));
    }

    [Fact]
    public async Task Load_shows_connections_models_and_the_default_without_saving()
    {
        var (vm, calls) = Create();

        await vm.LoadAsync(Ct);

        Assert.Equal(2, vm.Connections.Count);
        Assert.Equal(3, vm.FilteredModels.Count);
        Assert.Equal("Claude Haiku 5.5", vm.DefaultModel!.Model.Name);
        Assert.Equal(("200k", "0.1 $ · 0.5 $"), (vm.DefaultModel.Context, vm.DefaultModel.Price));
        Assert.Equal("gratuit", vm.FilteredModels.Single(m => m.Model.Id == "qwen3:8b").Price);
        Assert.All(calls, c => Assert.Equal(HttpMethod.Get, c.Method));
    }

    [Fact]
    public async Task The_picker_searches_by_name_and_provider_then_saves_the_choice()
    {
        var (vm, calls) = Create();
        await vm.LoadAsync(Ct);
        vm.OpenPickerCommand.Execute(null);

        vm.ModelQuery = "sonnet";
        Assert.Equal("claude-sonnet-5-5", Assert.Single(vm.FilteredModels).Model.Id);
        vm.ModelQuery = "ollama";
        var local = Assert.Single(vm.FilteredModels);

        await vm.ChooseCommand.ExecuteAsync(local);

        var put = Assert.Single(calls, c => c.Method == HttpMethod.Put);
        Assert.Equal("/llm/settings", put.Path);
        Assert.Contains($"\"connectionId\":\"{OllamaId}\"", put.Body);
        Assert.Contains("\"model\":\"qwen3:8b\"", put.Body);
        Assert.False(vm.IsPickerOpen);
    }

    [Fact]
    public async Task Each_agent_shows_its_own_model_and_can_get_a_new_one_or_go_back_to_the_default()
    {
        var (vm, calls) = Create();
        await vm.LoadAsync(Ct);
        Assert.Equal(["qwen3:8b", "Par défaut"], vm.Agents.Select(a => a.Label));

        vm.OpenAgentPickerCommand.Execute(vm.Agents[1]);
        await vm.ChooseCommand.ExecuteAsync(vm.FilteredModels.Single(m => m.Model.Id == "claude-sonnet-5-5"));
        await vm.ResetAgentCommand.ExecuteAsync(vm.Agents[0]);

        var puts = calls.Where(c => c.Method == HttpMethod.Put).ToList();
        Assert.Equal(("/agents/redacteur/model", "/agents/analyste/model"), (puts[0].Path, puts[1].Path));
        Assert.Contains("\"model\":\"claude-sonnet-5-5\"", puts[0].Body);
        Assert.Contains("\"model\":null", puts[1].Body);
        Assert.DoesNotContain(calls, c => c.Path == LlmProtocol.Settings);
    }

    [Fact]
    public async Task The_free_chain_only_offers_free_models_and_can_be_filled_reordered_and_switched_on()
    {
        var (vm, calls) = Create();
        await vm.LoadAsync(Ct);

        vm.OpenChainPickerCommand.Execute(null);
        Assert.Equal(["qwen3:8b"], vm.FilteredModels.Select(m => m.Model.Id));
        await vm.ChooseCommand.ExecuteAsync(vm.FilteredModels[0]);
        await vm.FillChainCommand.ExecuteAsync(null);
        vm.FreeMode = true;
        for (var i = 0; i < 50 && calls.Count(c => c.Path == LlmProtocol.Free) < 3; i++)
            await Task.Delay(10, Ct);

        var free = calls.Where(c => c.Path == LlmProtocol.Free).Select(c => c.Body!).ToList();
        Assert.Equal(3, free.Count);
        Assert.Contains("\"enabled\":false", free[0]);
        Assert.Contains("\"model\":\"qwen3:8b\"", free[0]);
        Assert.DoesNotContain("claude", free[1]); // « Tout ajouter » ne prend que les modèles gratuits
        Assert.Contains("\"enabled\":true", free[2]);
        // Le sélecteur rouvert normalement montre de nouveau tous les modèles.
        vm.OpenPickerCommand.Execute(null);
        Assert.Equal(3, vm.FilteredModels.Count);
    }

    [Fact]
    public async Task Connecting_a_provider_sends_the_key_once_forgets_it_then_opens_the_picker()
    {
        var (vm, calls) = Create();
        await vm.LoadAsync(Ct);
        vm.OpenConnectCommand.Execute(null);
        vm.PickCommand.Execute(Providers[0]);
        Assert.True(vm.NeedsKey);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        vm.NewKey = "sk-ant-secret-0123456789WXYZ";

        await vm.ConnectCommand.ExecuteAsync(null);

        var post = Assert.Single(calls, c => c.Method == HttpMethod.Post);
        Assert.Contains("sk-ant-secret-0123456789WXYZ", post.Body);
        Assert.Equal("", vm.NewKey);
        Assert.Contains("chiffrée", vm.Info);
        Assert.True(vm.IsPickerOpen);
        Assert.Equal("Anthropic", vm.ModelQuery);
    }

    [Fact]
    public async Task A_local_server_needs_an_address_but_no_key()
    {
        var (vm, calls) = Create();
        vm.PickCommand.Execute(Providers[1]);

        Assert.Equal((false, true, "http://localhost:11434/v1"), (vm.NeedsKey, vm.HasAddress, vm.NewBaseUrl));
        Assert.True(vm.ConnectCommand.CanExecute(null));
        await vm.ConnectCommand.ExecuteAsync(null);

        var post = Assert.Single(calls, c => c.Method == HttpMethod.Post);
        Assert.Contains("\"apiKey\":null", post.Body);
        Assert.Contains("\"baseUrl\":\"http://localhost:11434/v1\"", post.Body);
    }

    [Fact]
    public async Task A_rejected_connection_shows_the_reason_and_the_key_is_still_forgotten()
    {
        var (vm, _) = Create(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { errors = new { apiKey = new[] { "La clé doit faire entre 16 et 512 caractères, sans espace." } } }),
                    System.Text.Encoding.UTF8, "application/problem+json"),
            }
            : null);
        vm.PickCommand.Execute(Providers[0]);
        vm.NewKey = "trop courte";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Contains("16 et 512", vm.Error);
        Assert.Equal("", vm.NewKey);
    }

    [Fact]
    public async Task Revoking_asks_for_confirmation_first()
    {
        var (vm, calls) = Create();
        await vm.LoadAsync(Ct);

        vm.AskRevokeCommand.Execute(vm.Connections[0]);
        Assert.DoesNotContain(calls, c => c.Method == HttpMethod.Delete);

        await vm.ConfirmRevokeCommand.ExecuteAsync(null);

        Assert.Equal($"/llm/connections/{AnthropicId}", Assert.Single(calls, c => c.Method == HttpMethod.Delete).Path);
        Assert.NotNull(vm.Connections[0].RevokedAt);
        Assert.Null(vm.Default);
        Assert.Null(vm.PendingRevoke);
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
