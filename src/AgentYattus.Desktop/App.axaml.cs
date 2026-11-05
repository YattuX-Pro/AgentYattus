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
            var vm = new HomeViewModel(new ApiClient(new HttpClient { BaseAddress = apiUrl }));
            var hub = new MissionsHubClient(apiUrl);
            hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => vm.Apply(e));
            _ = ConnectAsync(hub, vm);
            desktop.ShutdownRequested += (_, _) => hub.DisposeAsync().AsTask().GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
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
