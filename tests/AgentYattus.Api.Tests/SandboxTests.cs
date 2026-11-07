using AgentYattus.Api.Modules.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentYattus.Api.Tests;

/// <summary>Le bac à sable (ADR 0015) : un conteneur jetable, le dossier du projet seul, sans réseau, borné en durée.</summary>
public sealed class SandboxTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-sandbox-").FullName;

    // alpine:3 : petite image, déjà présente sur la machine de développement ; sinon le test est ignoré.
    private static DockerSandbox Sandbox(int timeout = 20) =>
        new(Options.Create(new AgentsOptions { SandboxImage = "alpine:3", SandboxTimeoutSeconds = timeout }), NullLogger<DockerSandbox>.Instance);

    private static bool DockerAvailable()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("docker", "image inspect alpine:3")
                { RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Fact]
    public void The_container_gets_the_project_folder_only_no_network_no_privileges()
    {
        var args = DockerSandbox.Arguments("n", "/srv/projet:piege", "dotnet test; echo fini", new AgentsOptions(), user: null);

        Assert.Contains("--rm", args);
        Assert.Equal("none", args[args.ToList().IndexOf("--network") + 1]);
        Assert.Contains("ALL", args);
        Assert.Contains("type=bind,source=/srv/projet:piege,target=/work", args);
        // La commande entière est UN argument, donné au shell du conteneur.
        Assert.Equal(["sh", "-c", "dotnet test; echo fini"], args.TakeLast(3));
    }

    [Fact]
    public async Task A_command_runs_in_the_project_folder_but_cannot_reach_the_network()
    {
        Assert.SkipUnless(DockerAvailable(), "Docker ou l'image alpine:3 n'est pas disponible.");

        var written = await Sandbox().RunAsync(_workspace, "echo bonjour > sortie.txt && cat sortie.txt && ls /", Ct);
        Assert.Equal(0, written!.ExitCode);
        Assert.Contains("bonjour", written.Output);
        Assert.Equal("bonjour\n", await File.ReadAllTextAsync(Path.Combine(_workspace, "sortie.txt"), Ct));
        Assert.DoesNotContain("Users", written.Output); // le disque de la machine n'est pas là

        var offline = await Sandbox().RunAsync(_workspace, "wget -q -T 3 -O - http://1.1.1.1", Ct);
        Assert.NotEqual(0, offline!.ExitCode);
    }

    [Fact]
    public async Task A_command_that_runs_too_long_is_killed()
    {
        Assert.SkipUnless(DockerAvailable(), "Docker ou l'image alpine:3 n'est pas disponible.");

        var result = await Sandbox(timeout: 3).RunAsync(_workspace, "sleep 60", Ct);

        Assert.True(result!.TimedOut);
    }
}
