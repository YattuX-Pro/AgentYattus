using System.Net;
using System.Net.Http.Headers;

namespace AgentYattus.Client.Core.Tests;

/// <summary>Le sidecar (ADR 0014) : l'API locale lancée par le logiciel, fermée à qui n'a pas le jeton du lancement.</summary>
public sealed class LocalBackendTests
{
    [Fact]
    public void Each_launch_gets_its_own_port_and_token_and_the_token_never_goes_on_the_command_line()
    {
        var a = LocalBackend.Prepare("/opt/app/api/AgentYattus.Api.dll");
        var b = LocalBackend.Prepare("/opt/app/api/AgentYattus.Api.dll");

        Assert.NotEqual(a.Token, b.Token);
        Assert.Equal(64, a.Token.Length);
        Assert.Equal("127.0.0.1", a.Url.Host);
        var info = a.StartInfo();
        Assert.Equal(("dotnet", "Local", a.Token), (info.FileName, info.Environment["Mode"], info.Environment["Local__Token"]));
        Assert.DoesNotContain(info.ArgumentList, arg => arg.Contains(a.Token));
    }

    [Fact]
    public async Task The_real_api_starts_answers_only_with_the_token_and_stops_with_the_app()
    {
        // La compilation du dépôt (la même que celle des tests) ; ignoré si elle n'est pas là.
        var path = LocalBackend.Find(AppContext.BaseDirectory, null, searchRepository: true);
        Assert.SkipWhen(path is null, "L'API n'est pas compilée.");
        // L'API hérite de l'environnement : sa base va dans un dossier temporaire, pas dans le vrai dossier de données.
        var data = Directory.CreateTempSubdirectory("yattus-sidecar-").FullName;
        Environment.SetEnvironmentVariable("Local__DataFolder", data);
        var local = LocalBackend.Prepare(path!);
        try
        {
            await local.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

            using var http = new HttpClient { BaseAddress = local.Url };
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("me", TestContext.Current.CancellationToken)).StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", local.Token);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("me", TestContext.Current.CancellationToken)).StatusCode);
        }
        finally
        {
            await local.DisposeAsync();
            Environment.SetEnvironmentVariable("Local__DataFolder", null);
        }
    }

    [Fact]
    public async Task A_program_squatting_the_port_never_receives_the_token()
    {
        var path = LocalBackend.Find(AppContext.BaseDirectory, null, searchRepository: true);
        Assert.SkipWhen(path is null, "L'API n'est pas compilée.");
        var local = LocalBackend.Prepare(path!);
        // Un intrus prend le port avant l'API et répond à tout, sans connaître le jeton.
        using var squatter = new HttpListener();
        squatter.Prefixes.Add(local.Url.AbsoluteUri);
        squatter.Start();
        var seen = new List<string?>();
        _ = Task.Run(async () =>
        {
            while (squatter.IsListening)
            {
                var context = await squatter.GetContextAsync();
                seen.Add(context.Request.Headers["Authorization"]);
                await context.Response.OutputStream.WriteAsync("faux"u8.ToArray(), TestContext.Current.CancellationToken);
                context.Response.Close();
            }
        }, TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => local.StartAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.All(seen, header => Assert.Null(header));
        }
        finally
        {
            await local.DisposeAsync();
        }
    }
}
