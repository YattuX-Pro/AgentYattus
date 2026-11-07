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
            if (LocalApi() is { } local)
            {
                StartLocal(desktop, local);
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
            var hub = new MissionsHubClient(apiUrl, accessTokenProvider: () => tokens.GetAccessTokenAsync());
            hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => _ = shell.ApplyAsync(e));
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
    private static LocalBackend? LocalApi()
    {
        var mode = Environment.GetEnvironmentVariable("AGENTYATTUS_MODE");
        if (string.Equals(mode, "server", StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("AGENTYATTUS_API_URL") is not null)
            return null;
        var asked = string.Equals(mode, "local", StringComparison.OrdinalIgnoreCase);
        var path = LocalBackend.Find(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("AGENTYATTUS_API_PATH"), searchRepository: asked);
        return path is null ? null : LocalBackend.Prepare(path);
    }

    private static void StartLocal(IClassicDesktopStyleApplicationLifetime desktop, LocalBackend local)
    {
        // Pas de connexion : la personne de la machine est la seule utilisatrice ; le jeton du lancement ne périme pas.
        var tokens = new TokenStore(new OidcClient(new HttpClient(), new OidcSettings(local.Url, "local")))
        {
            Session = new OidcSession(local.Token, null, DateTimeOffset.MaxValue, Environment.UserName, ["llm-admin"]),
        };
        var api = new ApiClient(new HttpClient(new BearerTokenHandler(tokens) { InnerHandler = new HttpClientHandler() }) { BaseAddress = local.Url });
        var home = new HomeViewModel(api);
        var shell = new ShellViewModel(api, home) { UserName = Environment.UserName };
        shell.OpenUrlRequested += uri => _ = OpenBrowserAsync(uri);
        var hub = new MissionsHubClient(local.Url, accessTokenProvider: () => Task.FromResult<string?>(local.Token));
        hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => _ = shell.ApplyAsync(e));
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
                await local.StartAsync(Portraits());
                await Dispatcher.UIThread.InvokeAsync(() => ConnectAsync(hub, home));
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                await Dispatcher.UIThread.InvokeAsync(() => home.Error = $"L'API locale n'a pas démarré : {ex.Message}");
            }
        });
    }

    /// <summary>La galerie de départ : « portraits/ » à côté du logiciel, sinon deploy/portraits du dépôt.</summary>
    private static string? Portraits()
    {
        if (Path.Combine(AppContext.BaseDirectory, "portraits") is var installed && Directory.Exists(installed))
            return installed;
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
