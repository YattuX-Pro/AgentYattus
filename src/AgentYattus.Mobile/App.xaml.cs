using AgentYattus.Client.Core;

namespace AgentYattus.Mobile;

public partial class App : Application
{
    private readonly HomeViewModel _vm;

    public App(HomeViewModel vm, MissionsHubClient hub)
    {
        InitializeComponent();
        _vm = vm;
        hub.MissionEventReceived += e => MainThread.BeginInvokeOnMainThread(() => vm.Apply(e));
        _ = ConnectAsync(hub, vm);
    }

    // La page est créée ici, après InitializeComponent : ses StaticResource viennent de App.xaml.
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage(_vm));

    // ponytail: une seule tentative au démarrage, comme le desktop ; la reconnexion automatique ne couvre que les coupures après connexion.
    private static async Task ConnectAsync(MissionsHubClient hub, HomeViewModel vm)
    {
        try
        {
            await hub.StartAsync();
            MainThread.BeginInvokeOnMainThread(() => vm.IsBackendConnected = true);
        }
        catch (HttpRequestException)
        {
        }
    }
}
