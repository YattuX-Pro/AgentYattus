using System.Diagnostics;
using System.Text;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>Ce qu'a donné une commande : son code de sortie et la fin de sa sortie.</summary>
internal sealed record CommandResult(int ExitCode, string Output, bool TimedOut);

/// <summary>Où les commandes des agents s'exécutent (ADR 0015) : jamais sur la machine elle-même.</summary>
internal interface ICommandSandbox
{
    /// <summary>Null si le bac à sable n'est pas disponible (pas de Docker).</summary>
    Task<CommandResult?> RunAsync(string workspace, string command, CancellationToken cancellationToken);
}

/// <summary>
/// Un conteneur Docker jetable par commande : le dossier du projet seul, sans réseau, sans privilège, borné en mémoire,
/// en processeurs, en processus et en durée (ADR 0015).
/// </summary>
internal sealed class DockerSandbox(Microsoft.Extensions.Options.IOptions<AgentsOptions> options, ILogger<DockerSandbox> logger) : ICommandSandbox
{
    public const int MaxOutputChars = 20_000;

    public async Task<CommandResult?> RunAsync(string workspace, string command, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var name = $"agent-yattus-{Guid.NewGuid():N}";
        var info = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        // Les arguments un par un : aucun shell de la machine n'interprète la commande, seul celui du conteneur.
        foreach (var arg in Arguments(name, workspace, command, settings, await UserAsync()))
            info.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning("Bac à sable indisponible : Docker introuvable.");
            return null;
        }

        using (process)
        {
            var output = new Tail(MaxOutputChars);
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Add(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Add(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(settings.SandboxTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                // Durée dépassée (ou mission arrêtée) : on tue le conteneur lui-même, pas seulement le client docker.
                await KillAsync(name);
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                if (cancellationToken.IsCancellationRequested)
                    throw;
                return new CommandResult(-1, output.ToString(), TimedOut: true);
            }
            // Docker n'a pas pu démarrer (démon arrêté, image absente) : codes 125 à 127, et rien de la commande.
            return new CommandResult(process.ExitCode, output.ToString(), TimedOut: false);
        }
    }

    internal static IReadOnlyList<string> Arguments(string name, string workspace, string command, AgentsOptions settings, string? user)
    {
        List<string> args =
        [
            "run", "--rm", "--name", name, "--label", "agent-yattus=sandbox",
            "--network", settings.SandboxNetwork is "bridge" ? "bridge" : "none",
            "--memory", "1g", "--cpus", "2", "--pids-limit", "256",
            "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            // --mount plutôt que -v : un chemin qui contient « : » ne peut pas changer le sens de l'option.
            "--mount", $"type=bind,source={Path.GetFullPath(workspace)},target=/work",
            "--workdir", "/work",
        ];
        if (user is not null)
            args.AddRange(["--user", user]);
        args.AddRange([settings.SandboxImage, "sh", "-c", command]);
        return args;
    }

    // Sous Linux, l'utilisateur de l'API plutôt que root : les fichiers créés lui appartiennent. Docker Desktop (macOS,
    // Windows) fait déjà cette correspondance.
    private static string? _user;
    private static async Task<string?> UserAsync()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        if (_user is not null)
            return _user;
        static async Task<string> IdAsync(string flag)
        {
            using var p = Process.Start(new ProcessStartInfo("id", flag) { RedirectStandardOutput = true })!;
            var text = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            return text;
        }
        return _user = $"{await IdAsync("-u")}:{await IdAsync("-g")}";
    }

    private static async Task KillAsync(string name)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "kill", name }, RedirectStandardOutput = true, RedirectStandardError = true });
            if (kill is not null)
                await kill.WaitForExitAsync();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>Garde la fin de la sortie : c'est là que sont les erreurs et le résumé des tests.</summary>
    private sealed class Tail(int max)
    {
        private readonly StringBuilder _text = new();
        private bool _cut;

        public void Add(string line)
        {
            lock (_text)
            {
                _text.Append(line).Append('\n');
                if (_text.Length > max)
                {
                    _text.Remove(0, _text.Length - max);
                    _cut = true;
                }
            }
        }

        public override string ToString()
        {
            lock (_text)
                return (_cut ? "[… début de la sortie coupé]\n" : "") + _text;
        }
    }
}
