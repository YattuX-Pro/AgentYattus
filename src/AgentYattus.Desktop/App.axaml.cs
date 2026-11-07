using AgentYattus.Client.Core;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AgentYattus.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Mode local (ADR 0014) : le logiciel lance sa propre API. Sinon, un serveur et Keycloak.
            if (LocalApi(out var developing) is { } local)
            {
                StartLocal(desktop, local, developing);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var apiUrl = new Uri(Environment.GetEnvironmentVariable("AGENTYATTUS_API_URL") ?? "http://localhost:5080/");
            var authority = new Uri(Environment.GetEnvironmentVariable("AGENTYATTUS_AUTHORITY")
                                    ?? "http://localhost:8380/realms/agent-yattus");
            var oidc = new OidcClient(new HttpClient(), new OidcSettings(authority, "agent-yattus-desktop"));
            var tokens = new TokenStore(oidc);
            // Chaque requête vers l'API porte le jeton de la session.
            var api = new ApiClient(new HttpClient(new BearerTokenHandler(tokens) { InnerHandler = new HttpClientHandler() })
            {
                BaseAddress = apiUrl,
            });
            var vm = new HomeViewModel(api);
            var shell = new ShellViewModel(api, vm, new LoginViewModel(oidc, tokens, OpenBrowserAsync))
            {
                AdminUrl = new Uri(Environment.GetEnvironmentVariable("AGENTYATTUS_ADMIN_URL") ?? "http://localhost:4200/"),
            };
            shell.OpenUrlRequested += uri => _ = OpenBrowserAsync(uri);
            tokens.Expired += () => Dispatcher.UIThread.Post(shell.SessionExpired);
            var hub = new MissionsHubClient(apiUrl, accessTokenProvider: () => tokens.GetAccessTokenAsync());
            hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => _ = shell.ApplyAsync(e));
            hub.MissionTextReceived += e => Dispatcher.UIThread.Post(() => shell.ApplyText(e));
            // Le hub exige un jeton : on s'y connecte après la connexion, on s'en détache à la déconnexion.
            shell.LoggedIn += () => _ = ConnectAsync(hub, vm);
            shell.LoggedOut += () => _ = hub.StopAsync();
            desktop.ShutdownRequested += (_, _) => hub.DisposeAsync().AsTask().GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow { DataContext = shell };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Le mode local s'il est demandé (AGENTYATTUS_MODE=local), ou si l'API est installée à côté du logiciel
    /// et qu'aucun serveur n'est indiqué. En développement, AGENTYATTUS_MODE=local prend la compilation du dépôt.
    /// </summary>
    private static LocalBackend? LocalApi(out bool developing)
    {
        developing = false;
        var mode = Environment.GetEnvironmentVariable("AGENTYATTUS_MODE");
        if (string.Equals(mode, "server", StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("AGENTYATTUS_API_URL") is not null)
            return null;
        var asked = string.Equals(mode, "local", StringComparison.OrdinalIgnoreCase);
        developing = asked;
        var path = LocalBackend.Find(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("AGENTYATTUS_API_PATH"), searchRepository: asked);
        return path is null ? null : LocalBackend.Prepare(path);
    }

    private static void StartLocal(IClassicDesktopStyleApplicationLifetime desktop, LocalBackend local, bool developing)
    {
        // Pas de connexion : la personne de la machine est la seule utilisatrice ; le jeton du lancement ne périme pas.
        // Il n'est attaché aux requêtes qu'une fois l'API vérifiée (StartAsync) : avant, aucune requête ne le porte.
        var tokens = new TokenStore(new OidcClient(new HttpClient(), new OidcSettings(local.Url, "local")));
        var api = new ApiClient(new HttpClient(new BearerTokenHandler(tokens) { InnerHandler = new HttpClientHandler() }) { BaseAddress = local.Url });
        var home = new HomeViewModel(api);
        var shell = new ShellViewModel(api, home) { UserName = Environment.UserName };
        shell.OpenUrlRequested += uri => _ = OpenBrowserAsync(uri);
        var hub = new MissionsHubClient(local.Url, accessTokenProvider: () => tokens.GetAccessTokenAsync());
        hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => _ = shell.ApplyAsync(e));
        hub.MissionTextReceived += e => Dispatcher.UIThread.Post(() => shell.ApplyText(e));
        desktop.ShutdownRequested += (_, _) =>
        {
            hub.DisposeAsync().AsTask().GetAwaiter().GetResult();
            local.DisposeAsync().AsTask().GetAwaiter().GetResult();
        };
        desktop.MainWindow = new MainWindow { DataContext = shell };
        _ = Task.Run(async () =>
        {
            try
            {
                await local.StartAsync(Portraits(developing));
                tokens.Session = new OidcSession(local.Token, null, DateTimeOffset.MaxValue, Environment.UserName, ["llm-admin"]);
                await Dispatcher.UIThread.InvokeAsync(() => ConnectAsync(hub, home));
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                await Dispatcher.UIThread.InvokeAsync(() => home.Error = $"L'API locale n'a pas démarré : {ex.Message}");
            }
        });
    }

    /// <summary>
    /// La galerie de départ : « portraits/ » à côté du logiciel. En développement seulement (AGENTYATTUS_MODE=local),
    /// deploy/portraits du dépôt : une version installée ne va jamais chercher dans les dossiers parents.
    /// </summary>
    private static string? Portraits(bool developing)
    {
        if (Path.Combine(AppContext.BaseDirectory, "portraits") is var installed && Directory.Exists(installed))
            return installed;
        if (!developing)
            return null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Path.Combine(dir.FullName, "deploy", "portraits") is var repo && Directory.Exists(repo))
                return repo;
        return null;
    }

    // Le navigateur du système : l'utilisateur s'y connecte, avec son gestionnaire de mots de passe et sa MFA.
    private static Task OpenBrowserAsync(Uri uri)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    // ponytail: une seule tentative au démarrage ; la reconnexion automatique ne couvre que les coupures après connexion.
    private static async Task ConnectAsync(MissionsHubClient hub, HomeViewModel vm)
    {
        try
        {
            await hub.StartAsync();
            vm.IsBackendConnected = true;
            // Après la connexion au hub : aucun changement de statut ne peut se glisser entre la liste et les événements.
            await vm.LoadAsync();
        }
        catch (HttpRequestException)
        {
            vm.IsBackendConnected = false;
        }
    }
}
