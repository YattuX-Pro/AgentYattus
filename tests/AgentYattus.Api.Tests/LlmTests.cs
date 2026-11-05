using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentYattus.Api.Tests;

/// <summary>La gestion du LLM : clés chiffrées et jamais renvoyées, modèle, révocation, réservée à llm-admin.</summary>
public sealed class LlmTests
{
    private const string Key = "sk-ant-test-0123456789abcdefWXYZ";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_added_key_is_stored_encrypted_and_never_sent_back()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();

        var added = await client.PostAsJsonAsync(LlmProtocol.Credentials, new AddLlmCredentialRequest("anthropic", "Clé perso", Key), Ct);
        var status = await client.GetAsync(LlmProtocol.Path, Ct);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.DoesNotContain(Key, await added.Content.ReadAsStringAsync(Ct));
        var json = await status.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(Key, json);
        var dto = (await status.Content.ReadFromJsonAsync<LlmStatusDto>(Ct))!;
        var credential = Assert.Single(dto.Credentials);
        Assert.Equal("WXYZ", credential.Last4);
        Assert.Equal("test", credential.CreatedBy);
        // La première clé devient active.
        Assert.True(credential.IsActive);

        using var scope = api.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<LlmDb>().Credentials.SingleAsync(Ct);
        Assert.DoesNotContain(Key, stored.ProtectedKey);
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("AgentYattus.Llm.ApiKey.v1");
        Assert.Equal(Key, protector.Unprotect(stored.ProtectedKey!));
    }

    [Fact]
    public async Task Revoking_erases_the_encrypted_key_and_deactivates_it()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var added = await (await client.PostAsJsonAsync(LlmProtocol.Credentials, new AddLlmCredentialRequest("anthropic", "À révoquer", Key), Ct))
            .Content.ReadFromJsonAsync<LlmCredentialDto>(Ct);

        var revoke = await client.DeleteAsync($"{LlmProtocol.Credentials}/{added!.Id}", Ct);
        var again = await client.DeleteAsync($"{LlmProtocol.Credentials}/{added.Id}", Ct);
        var unknown = await client.DeleteAsync($"{LlmProtocol.Credentials}/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var status = (await client.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!;
        Assert.Null(status.ActiveCredentialId);
        Assert.NotNull(Assert.Single(status.Credentials).RevokedAt);
        using var scope = api.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<LlmDb>().Credentials.SingleAsync(Ct)).ProtectedKey);
    }

    [Fact]
    public async Task The_model_must_exist_and_the_key_must_not_be_revoked()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var key = await (await client.PostAsJsonAsync(LlmProtocol.Credentials, new AddLlmCredentialRequest("anthropic", "Clé", Key), Ct))
            .Content.ReadFromJsonAsync<LlmCredentialDto>(Ct);

        var unknownModel = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(key!.Id, "gpt-9"), Ct);
        var ok = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(key.Id, "claude-sonnet-5-5"), Ct);
        await client.DeleteAsync($"{LlmProtocol.Credentials}/{key.Id}", Ct);
        var revokedKey = await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(key.Id, "claude-sonnet-5-5"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, unknownModel.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal("claude-sonnet-5-5", (await client.GetFromJsonAsync<LlmStatusDto>(LlmProtocol.Path, Ct))!.Model);
        Assert.Equal(HttpStatusCode.BadRequest, revokedKey.StatusCode);
    }

    [Theory]
    [InlineData("pas-une-cle")]
    [InlineData("sk-ant avec des espaces 1234567")]
    public async Task Invalid_keys_are_refused(string apiKey)
    {
        await using var api = new ApiFactory();

        var response = await api.CreateClient().PostAsJsonAsync(LlmProtocol.Credentials, new AddLlmCredentialRequest("anthropic", "x", apiKey), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_llm_admins_can_manage_keys()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "observateur");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(LlmProtocol.Path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(LlmProtocol.Credentials, new AddLlmCredentialRequest("anthropic", "x", Key), Ct)).StatusCode);
        // Le reste de l'application reste accessible.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/missions", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_agents_use_the_active_key_then_the_configuration_then_nothing()
    {
        await using var api = new ApiFactory();
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LlmDb>();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        IConfiguration Config(string? key) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = key }).Build();

        Assert.Null(await new LlmClientProvider(db, protection, Config(null)).GetAsync(Ct));
        Assert.NotNull(await new LlmClientProvider(db, protection, Config("sk-ant-config-0123456789")).GetAsync(Ct));

        var client = api.CreateClient();
        var key = await (await client.PostAsJsonAsync(LlmProtocol.Credentials, new AddLlmCredentialRequest("anthropic", "Clé", Key), Ct))
            .Content.ReadFromJsonAsync<LlmCredentialDto>(Ct);
        await client.PutAsJsonAsync(LlmProtocol.Settings, new UpdateLlmSettingsRequest(key!.Id, "claude-opus-5-5"), Ct);
        db.ChangeTracker.Clear();

        var active = await new LlmClientProvider(db, protection, Config(null)).GetAsync(Ct);
        Assert.Equal("claude-opus-5-5", active!.Model);
    }
}
