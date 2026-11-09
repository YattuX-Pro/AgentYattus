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
            var selection = BackendSelection.ForStartup();
            if (selection.IsLocal)
            {
                var configured = Environment.GetEnvironmentVariable("AGENTYATTUS_API_PATH");
                var installed = LocalBackend.Find(AppContext.BaseDirectory, configured, searchRepository: false);
                var path = installed ?? LocalBackend.Find(AppContext.BaseDirectory, configured, searchRepository: true);
                if (path is null)
                    throw new InvalidOperationException("L'API locale est introuvable. Installez la version complète ou compilez l'API.");
                StartLocal(desktop, LocalBackend.Prepare(path), developing: installed is null);
            }
            else
                StartServer(desktop, selection);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void StartServer(IClassicDesktopStyleApplicationLifetime desktop, BackendSelection selection)
    {
        var apiUrl = new Uri(selection.ApiUrl);
        var authority = new Uri(selection.Authority);
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
            AdminUrl = new Uri(selection.AdminUrl),
            DataLocation = "Serveur · " + apiUrl.GetLeftPart(UriPartial.Authority),
            DataLocationDetail = apiUrl.AbsoluteUri,
        };
        shell.OpenUrlRequested += uri => _ = OpenBrowserAsync(uri);
        tokens.Expired += () => Dispatcher.UIThread.Post(shell.SessionExpired);
        var hub = new MissionsHubClient(apiUrl, accessTokenProvider: () => tokens.GetAccessTokenAsync());
        hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => _ = shell.ApplyAsync(e));
        hub.MissionTextReceived += e => Dispatcher.UIThread.Post(() => shell.ApplyText(e));
        hub.MissionActivityReceived += e => Dispatcher.UIThread.Post(() => shell.ApplyActivity(e));
        hub.Reconnected += () => Dispatcher.UIThread.Post(() => _ = shell.RefreshConversationsAsync());
        // Le hub exige un jeton : on s'y connecte après la connexion, on s'en détache à la déconnexion.
        shell.LoggedIn += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (desktop.MainWindow is not { } window)
                    return;
                if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
                    window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Show();
                window.Activate();
            });
            _ = ConnectAsync(hub, vm);
        };
        shell.LoggedOut += () => _ = hub.StopAsync();
        desktop.ShutdownRequested += (_, _) => hub.DisposeAsync().AsTask().GetAwaiter().GetResult();
        desktop.MainWindow = new MainWindow { DataContext = shell };
    }

    private static void StartLocal(IClassicDesktopStyleApplicationLifetime desktop, LocalBackend local, bool developing)
    {
        // Pas de connexion : la personne de la machine est la seule utilisatrice ; le jeton du lancement ne périme pas.
        // Il n'est attaché aux requêtes qu'une fois l'API vérifiée (StartAsync) : avant, aucune requête ne le porte.
        var tokens = new TokenStore(new OidcClient(new HttpClient(), new OidcSettings(local.Url, "local")));
        var api = new ApiClient(new HttpClient(new BearerTokenHandler(tokens) { InnerHandler = new HttpClientHandler() }) { BaseAddress = local.Url });
        var home = new HomeViewModel(api);
        var shell = new ShellViewModel(api, home)
        {
            UserName = Environment.UserName,
            DataLocation = "Sur cet ordinateur · base locale",
            DataLocationDetail = Path.Combine(Environment.GetEnvironmentVariable("Local__DataFolder") ?? BackendSelection.DataFolder, "agent-yattus.db"),
        };
        shell.OpenUrlRequested += uri => _ = OpenBrowserAsync(uri);
        var hub = new MissionsHubClient(local.Url, accessTokenProvider: () => tokens.GetAccessTokenAsync());
        hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => _ = shell.ApplyAsync(e));
        hub.MissionTextReceived += e => Dispatcher.UIThread.Post(() => shell.ApplyText(e));
        hub.MissionActivityReceived += e => Dispatcher.UIThread.Post(() => shell.ApplyActivity(e));
        hub.Reconnected += () => Dispatcher.UIThread.Post(() => _ = shell.RefreshConversationsAsync());
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
