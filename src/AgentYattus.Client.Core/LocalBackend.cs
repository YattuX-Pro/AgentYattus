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
    private StreamWriter? _log;
    private long _logged;

    /// <summary>
    /// Le journal de l'API locale : là où l'on trouve la cause d'une mission en échec. Réécrit à chaque lancement,
    /// 5 Mo au plus. Il ne contient pas le jeton (passé par l'environnement) ; il peut citer des erreurs des fournisseurs.
    /// </summary>
    public static string LogPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentYattus", "api.log");

    public const long MaxLogBytes = 5 * 1024 * 1024;

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

    /// <summary>
    /// Lance l'API et attend qu'elle prouve connaître le jeton (30 secondes au plus). Ce n'est qu'ensuite que le
    /// logiciel envoie son jeton : un programme qui aurait pris le port entre-temps ne le reçoit jamais.
    /// </summary>
    public async Task StartAsync(string? seedFolder = null, CancellationToken cancellationToken = default)
    {
        _process = Process.Start(StartInfo(seedFolder)) ?? throw new InvalidOperationException("L'API locale n'a pas démarré.");
        // Les sorties sont lues pour ne pas bloquer l'API quand le tampon est plein ; elles ne contiennent pas le jeton.
        // Elles vont dans api.log : c'est le « journal du serveur » d'un logiciel en mode local.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            _log = new StreamWriter(LogPath, append: false) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log = null; // sans journal, le logiciel fonctionne quand même
        }
        _process.OutputDataReceived += (_, e) => Log(e.Data);
        _process.ErrorDataReceived += (_, e) => Log(e.Data);
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
                var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                using var response = await http.GetAsync($"health/local?nonce={nonce}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var expected = Convert.ToHexString(HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Token), System.Text.Encoding.UTF8.GetBytes(nonce)));
                    var proof = await response.Content.ReadAsStringAsync(cancellationToken);
                    // Quelqu'un répond sur ce port sans connaître le jeton : on ne lui parle plus.
                    if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(proof.Trim()), System.Text.Encoding.ASCII.GetBytes(expected))
                        || _process.HasExited)
                        throw new InvalidOperationException("Un autre programme répond sur le port de l'API locale : démarrage annulé.");
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Pas encore prête.
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("L'API locale ne répond pas après 30 secondes.");
    }

    private void Log(string? line)
    {
        if (line is null || _log is null)
            return;
        lock (_log)
        {
            if (_logged > MaxLogBytes)
                return;
            _logged += line.Length + 1;
            try
            {
                _log.WriteLine(_logged > MaxLogBytes ? "[… journal coupé à 5 Mo]" : line);
            }
            catch (ObjectDisposedException)
            {
                // Une dernière ligne après l'arrêt du logiciel : rien à garder.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
            _process.Kill(entireProcessTree: true);
        _process?.Dispose();
        if (_log is { } log)
            lock (log)
                log.Dispose();
        return ValueTask.CompletedTask;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
