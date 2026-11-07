using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentYattus.Api.Tests;

/// <summary>Le mode local (ADR 0014) : SQLite dans le dossier de données, et le jeton du lancement à la place de Keycloak.</summary>
public sealed class LocalModeTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Token = "un-jeton-de-lancement-de-plus-de-trente-deux-caracteres";

    private readonly string _data = Directory.CreateTempSubdirectory("yattus-local-").FullName;
    private WebApplicationFactory<Program> _api = null!;

    public ValueTask InitializeAsync()
    {
        // Le vrai démarrage, sans rien remplacer : ni la base, ni l'authentification. Seul le modèle reste absent.
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Mode", "Local")
            .UseSetting("Local:Token", Token)
            .UseSetting("Local:DataFolder", _data)
            .UseSetting("Llm:CatalogUrl", "")
            .UseSetting("Team:SeedFolder", "")
            .ConfigureServices(services =>
            {
                services.RemoveAll<ILlmClientProvider>();
                services.AddSingleton<ILlmClientProvider>(new FixedLlmClientProvider(null));
            }));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private HttpClient Client(string? token)
    {
        var client = _api.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Only_the_launch_token_opens_the_api()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client(null).GetAsync("/health", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync(IdentityProtocol.Me, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(Token + "x").GetAsync(IdentityProtocol.Me, Ct)).StatusCode);

        // La personne de la machine : seule utilisatrice, administratrice de son instance.
        var me = (await Client(Token).GetFromJsonAsync<MeDto>(IdentityProtocol.Me, Ct))!;
        Assert.Equal(("local", true), (me.Id, me.IsInstanceAdmin));
    }

    [Fact]
    public async Task Every_module_keeps_its_tables_in_one_sqlite_file_in_the_data_folder()
    {
        var projects = (await Client(Token).GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!;

        Assert.Single(projects, p => p.IsPersonalDefault);
        Assert.True(File.Exists(Path.Combine(_data, "agent-yattus.db")));
        Assert.Equal(HttpStatusCode.OK, (await Client(Token).GetAsync(SkillsProtocol.Path, Ct)).StatusCode);
    }
}
