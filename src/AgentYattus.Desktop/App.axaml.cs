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
