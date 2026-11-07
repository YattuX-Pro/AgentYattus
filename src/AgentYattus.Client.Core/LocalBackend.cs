using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace AgentYattus.Client.Core;

/// <summary>
/// Le mode local (ADR 0014) : le logiciel lance l'API à côté de lui (sidecar), sur la boucle locale,
/// avec un jeton tiré au hasard à chaque lancement. L'API s'arrête avec le logiciel.
/// </summary>
public sealed class LocalBackend : IAsyncDisposable
{
    private Process? _process;

    private LocalBackend(string apiPath, int port, string token)
    {
        ApiPath = apiPath;
        Url = new Uri($"http://127.0.0.1:{port}/");
        Token = token;
    }

    public string ApiPath { get; }
    public Uri Url { get; }

    /// <summary>256 bits au hasard : la seule porte d'entrée de l'API locale. Jamais écrit sur le disque.</summary>
    public string Token { get; }

    /// <summary>Le port et le jeton sont choisis tout de suite : les clients HTTP peuvent être créés avant le démarrage.</summary>
    public static LocalBackend Prepare(string apiPath) =>
        new(apiPath, FreePort(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    /// <summary>
    /// Où trouver l'API : AGENTYATTUS_API_PATH, puis « api/ » à côté du logiciel (version installée),
    /// puis, si <paramref name="searchRepository"/>, la sortie de compilation du dépôt (développement).
    /// </summary>
    public static string? Find(string appDirectory, string? configured, bool searchRepository)
    {
        if (configured is { Length: > 0 })
            return File.Exists(configured) ? configured : null;
        foreach (var name in new[] { "AgentYattus.Api", "AgentYattus.Api.exe", "AgentYattus.Api.dll" })
            if (Path.Combine(appDirectory, "api", name) is var installed && File.Exists(installed))
                return installed;
        if (!searchRepository)
            return null;
        for (var dir = new DirectoryInfo(appDirectory); dir is not null; dir = dir.Parent)
            if (Path.Combine(dir.FullName, "src", "AgentYattus.Api", "bin", "Debug", "net10.0", "AgentYattus.Api.dll") is var built
                && File.Exists(built))
                return built;
        return null;
    }

    /// <summary>Ce que reçoit l'API : le mode, le jeton (par l'environnement, pas la ligne de commande), l'adresse, le parent.</summary>
    public ProcessStartInfo StartInfo(string? seedFolder = null)
    {
        var dll = ApiPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo(dll ? "dotnet" : ApiPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(ApiPath)!,
        };
        if (dll)
            info.ArgumentList.Add(ApiPath);
        info.Environment["Mode"] = "Local";
        info.Environment["Local__Token"] = Token;
        info.Environment["Local__ParentPid"] = Environment.ProcessId.ToString();
        info.Environment["ASPNETCORE_URLS"] = Url.AbsoluteUri.TrimEnd('/');
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        if (seedFolder is not null)
            info.Environment["Team__SeedFolder"] = seedFolder;
        return info;
    }

    /// <summary>Lance l'API et attend qu'elle réponde sur /health (30 secondes au plus).</summary>
    public async Task StartAsync(string? seedFolder = null, CancellationToken cancellationToken = default)
    {
        _process = Process.Start(StartInfo(seedFolder)) ?? throw new InvalidOperationException("L'API locale n'a pas démarré.");
        // Les sorties sont lues pour ne pas bloquer l'API quand le tampon est plein ; elles ne contiennent pas le jeton.
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var http = new HttpClient { BaseAddress = Url, Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"L'API locale s'est arrêtée au démarrage (code {_process.ExitCode}).");
            try
            {
                if ((await http.GetAsync("health", cancellationToken)).IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Pas encore prête.
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("L'API locale ne répond pas après 30 secondes.");
    }

    public ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
            _process.Kill(entireProcessTree: true);
        _process?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
