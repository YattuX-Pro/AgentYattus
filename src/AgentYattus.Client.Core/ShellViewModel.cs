using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>La coquille : l'écran affiché et la navigation entre écrans.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ApiClient _api;

    /// <param name="login">L'écran de connexion ; sans lui (tests), on démarre directement sur l'accueil.</param>
    public ShellViewModel(ApiClient api, HomeViewModel home, LoginViewModel? login = null)
    {
        _api = api;
        Home = home;
        Login = login;
        Current = login is null ? home : login;
        IsAuthenticated = login is null;
        home.MissionOpened += mission => _ = OpenMissionAsync(mission);
        if (login is not null)
            login.LoggedIn += session =>
            {
                UserName = session.UserName;
                IsLlmAdmin = session.IsLlmAdmin;
                IsAuthenticated = true;
                Current = Home;
                LoggedIn?.Invoke();
            };
    }

    public HomeViewModel Home { get; }

    public LoginViewModel? Login { get; }

    [ObservableProperty]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    public partial string? UserName { get; set; }

    /// <summary>Vrai si l'utilisateur a le rôle llm-admin : il peut gérer les clés et le modèle.</summary>
    [ObservableProperty]
    public partial bool IsLlmAdmin { get; set; }

    /// <summary>Pour l'application : se connecter au hub après la connexion, s'en détacher à la déconnexion.</summary>
    public event Action? LoggedIn;
    public event Action? LoggedOut;

    [RelayCommand]
    private void Logout()
    {
        if (Login is null)
            return;
        Login.Logout();
        IsAuthenticated = false;
        IsLlmAdmin = false;
        UserName = null;
        Current = Login;
        LoggedOut?.Invoke();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissionsSection), nameof(IsAgentsSection), nameof(IsReportsSection), nameof(IsSettingsSection))]
    public partial ObservableObject Current { get; set; }

    public bool IsMissionsSection => Current is HomeViewModel or MissionViewModel;
    public bool IsAgentsSection => Current is AgentsViewModel;
    public bool IsReportsSection => Current is ReportsViewModel;
    public bool IsSettingsSection => Current is SettingsViewModel;

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
    private async Task ShowSettingsAsync()
    {
        var screen = new SettingsViewModel(_api);
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
