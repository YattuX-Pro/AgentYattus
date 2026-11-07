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
        home.RecruitRequested += project => _ = CustomizeAsync(project, null, "Accueil", () => Current = Home);
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
    [NotifyPropertyChangedFor(nameof(ShowNav))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    public partial string? UserName { get; set; }

    /// <summary>Vrai si l'utilisateur a le rôle llm-admin : le bouton de l'espace admin web lui est proposé.</summary>
    [ObservableProperty]
    public partial bool IsLlmAdmin { get; set; }

    /// <summary>L'espace admin web (ADR 0008) : la gestion (modèles, agents, projets, réglages) se fait là, plus ici.</summary>
    public Uri AdminUrl { get; init; } = new("http://localhost:4200/");

    /// <summary>Pour l'application : ouvrir une adresse dans le navigateur du système.</summary>
    public event Action<Uri>? OpenUrlRequested;

    [RelayCommand]
    private void OpenAdmin() => OpenUrlRequested?.Invoke(AdminUrl);

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
    [NotifyPropertyChangedFor(nameof(IsMissionsSection), nameof(IsOfficeSection), nameof(IsAgentsSection), nameof(IsReportsSection), nameof(ShowNav))]
    public partial ObservableObject Current { get; set; }

    public bool IsMissionsSection => Current is HomeViewModel;
    public bool IsOfficeSection => Current is OfficeViewModel or RecruitViewModel;
    public bool IsAgentsSection => Current is AgentsViewModel;
    public bool IsReportsSection => Current is ReportsViewModel;

    /// <summary>Une mission s'ouvre dans l'open space de son projet (ADR 0012).</summary>
    public async Task OpenMissionAsync(MissionItem mission)
    {
        var screen = new OfficeViewModel(_api, mission, UserName ?? "vous")
        {
            ProjectName = mission.ProjectId is { } id ? ProjectName(id) : null,
        };
        screen.RecruitRequested += project => _ = RecruitAsync(screen, project);
        Current = screen;
        await screen.LoadAsync();
    }

    /// <summary>L'écran Nouveau collègue ; une fois fait (ou annulé), on revient à l'open space, qui recharge son équipe.</summary>
    private async Task RecruitAsync(OfficeViewModel office, Guid project)
    {
        var recruit = new RecruitViewModel(_api, project, projectName: ProjectName(project));
        recruit.Done += () =>
        {
            Current = office;
            _api.Faces.Forget(project);
            _ = office.ReloadTeamAsync();
            _ = Home.LoadCrewAsync();
        };
        Current = recruit;
        await recruit.LoadAsync();
    }

    private string? ProjectName(Guid project) => Home.Projects.FirstOrDefault(p => p.Id == project) is { IsPersonalDefault: false } p ? p.Name : null;

    /// <summary>« Nouveau collègue » et l'open space ont leur propre barre (maquette) : la navigation se cache.</summary>
    public bool ShowNav => IsAuthenticated && Current is not (RecruitViewModel or OfficeViewModel);

    [RelayCommand]
    private void GoHome() => Current = Home;

    /// <summary>L'open space : celui de la mission qui attend une décision ou qui tourne, sinon de la plus récente.</summary>
    [RelayCommand]
    private Task OpenOfficeAsync() =>
        (Home.Missions.FirstOrDefault(m => m.IsAwaitingApproval) ?? Home.Missions.FirstOrDefault(m => m.IsActive) ?? Home.Missions.FirstOrDefault())
            is { } mission ? OpenMissionAsync(mission) : Task.CompletedTask;

    [RelayCommand]
    private async Task ShowAgentsAsync()
    {
        var screen = new AgentsViewModel(_api, Home.SelectedProject?.Id, Home.SelectedProject?.Name, Home.Missions);
        // « Confier une mission à Inès » : retour à l'accueil, Inès choisie.
        screen.AssignRequested += agent =>
        {
            Home.WriteReport = false;
            Home.SelectedAgent = Home.Agents.FirstOrDefault(a => a.Key == agent.Key) ?? Home.SelectedAgent;
            Current = Home;
        };
        screen.CustomizeRequested += (project, card) => _ = CustomizeAsync(project, card, "Agents", () => _ = ShowAgentsAsync());
        Current = screen;
        await screen.LoadAsync();
    }

    /// <summary>« Nouveau collègue », sur un poste donné (le crayon d'une fiche) ou au choix ; ensuite, visages relus et retour.</summary>
    private async Task CustomizeAsync(Guid project, AgentCard? card, string backTo, Action back)
    {
        var recruit = new RecruitViewModel(_api, project, card?.Agent.Key, card?.Colleague, ProjectName(project)) { BackTo = backTo };
        recruit.Done += () =>
        {
            _api.Faces.Forget(project);
            _ = Home.LoadCrewAsync();
            back();
        };
        Current = recruit;
        await recruit.LoadAsync();
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
        if (Current is OfficeViewModel office)
            await office.ApplyAsync(e);
    }
}
