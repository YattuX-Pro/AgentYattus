using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>La coquille : l'écran affiché et la navigation entre écrans.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ShellViewModel(ApiClient api, HomeViewModel home)
    {
        _api = api;
        Home = home;
        Current = home;
        home.MissionOpened += mission => _ = OpenMissionAsync(mission);
    }

    public HomeViewModel Home { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissionsSection), nameof(IsAgentsSection), nameof(IsReportsSection))]
    public partial ObservableObject Current { get; set; }

    public bool IsMissionsSection => Current is HomeViewModel or MissionViewModel;
    public bool IsAgentsSection => Current is AgentsViewModel;
    public bool IsReportsSection => Current is ReportsViewModel;

    public async Task OpenMissionAsync(MissionItem mission)
    {
        var screen = new MissionViewModel(_api, mission);
        Current = screen;
        await screen.LoadAsync();
    }

    [RelayCommand]
    private void GoHome() => Current = Home;

    [RelayCommand]
    private async Task ShowAgentsAsync()
    {
        var screen = new AgentsViewModel(_api);
        Current = screen;
        await screen.LoadAsync();
    }

    [RelayCommand]
    private async Task ShowReportsAsync()
    {
        var screen = new ReportsViewModel(_api);
        Current = screen;
        await screen.LoadAsync();
    }

    /// <summary>Appelé sur le thread UI pour chaque événement du hub.</summary>
    public async Task ApplyAsync(MissionEventDto e)
    {
        Home.Apply(e);
        if (Current is MissionViewModel mission)
            await mission.ApplyAsync(e);
    }
}
