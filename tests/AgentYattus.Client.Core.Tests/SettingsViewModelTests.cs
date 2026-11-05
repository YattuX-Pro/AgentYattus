using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class SettingsViewModelTests
{
    private static readonly Guid KeyId = Guid.NewGuid();

    private static LlmStatusDto Status(bool revoked = false) => new(
        [new LlmProviderDto("anthropic", "Anthropic",
            [new LlmModelDto("claude-haiku-5-5", "Claude Haiku 5.5", 0.10m, 0.50m), new LlmModelDto("claude-sonnet-5-5", "Claude Sonnet 5.5", 2m, 10m)])],
        [new LlmCredentialDto(KeyId, "anthropic", "Clé perso", "WXYZ", DateTimeOffset.UtcNow, "dev", revoked ? DateTimeOffset.UtcNow : null, !revoked)],
        revoked ? null : KeyId, "claude-haiku-5-5");

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
            return request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Status(revoked)) }
                : new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
        });
        return (new SettingsViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") })), calls);
    }

    [Fact]
    public async Task Load_shows_models_keys_and_the_current_model_without_saving()
    {
        var (vm, calls) = Create();

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, vm.Models.Count);
        Assert.Equal("claude-haiku-5-5", vm.SelectedModel!.Id);
        Assert.Equal("WXYZ", Assert.Single(vm.Credentials).Last4);
        Assert.Equal(KeyId, vm.ActiveCredentialId);
        // Charger ne doit pas déclencher d'enregistrement.
        Assert.All(calls, c => Assert.Equal(HttpMethod.Get, c.Method));
    }

    [Fact]
    public async Task Choosing_a_model_saves_it_with_the_active_key()
    {
        var (vm, calls) = Create();
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.SelectedModel = vm.Models[1];
        for (var i = 0; i < 50 && vm.Info is null; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        var put = Assert.Single(calls, c => c.Method == HttpMethod.Put);
        Assert.Equal("/llm/settings", put.Path);
        Assert.Contains("\"model\":\"claude-sonnet-5-5\"", put.Body);
        Assert.Contains($"\"credentialId\":\"{KeyId}\"", put.Body);
    }

    [Fact]
    public async Task Adding_a_key_sends_it_once_then_forgets_it()
    {
        var (vm, calls) = Create();
        vm.NewLabel = "Clé perso";
        vm.NewKey = "sk-ant-secret-0123456789WXYZ";

        await vm.AddCommand.ExecuteAsync(null);

        var post = Assert.Single(calls, c => c.Method == HttpMethod.Post);
        Assert.Contains("sk-ant-secret-0123456789WXYZ", post.Body);
        Assert.Equal("", vm.NewKey);
        Assert.Contains("chiffrée", vm.Info);
    }

    [Fact]
    public async Task A_rejected_key_shows_the_reason_and_is_still_forgotten()
    {
        var (vm, _) = Create(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { errors = new { apiKey = new[] { "La clé doit faire entre 16 et 512 caractères, sans espace." } } }),
                    System.Text.Encoding.UTF8, "application/problem+json"),
            }
            : null);
        vm.NewLabel = "x";
        vm.NewKey = "trop courte";

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Contains("16 et 512", vm.Error);
        Assert.Equal("", vm.NewKey);
    }

    [Fact]
    public async Task Revoking_asks_for_confirmation_first()
    {
        var (vm, calls) = Create();
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.AskRevokeCommand.Execute(vm.Credentials[0]);
        Assert.DoesNotContain(calls, c => c.Method == HttpMethod.Delete);

        await vm.ConfirmRevokeCommand.ExecuteAsync(null);

        Assert.Equal($"/llm/credentials/{KeyId}", Assert.Single(calls, c => c.Method == HttpMethod.Delete).Path);
        Assert.NotNull(vm.Credentials[0].RevokedAt);
        Assert.Null(vm.PendingRevoke);
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
