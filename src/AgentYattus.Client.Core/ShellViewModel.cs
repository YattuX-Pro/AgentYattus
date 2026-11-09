using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>La coquille : l'écran affiché et la navigation entre écrans.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly Dictionary<Guid, WorkspaceViewModel> _workspaces = [];
    private bool _refreshing;
    public ConversationInbox Inbox { get; private set; } = null!;

    /// <param name="login">L'écran de connexion ; sans lui (tests), on démarre directement sur l'accueil.</param>
    public ShellViewModel(ApiClient api, HomeViewModel home, LoginViewModel? login = null)
    {
        _api = api;
        Home = home;
        Inbox = new ConversationInbox(api)
        {
            IsReadOnScreen = notice => Current is WorkspaceViewModel workspace && workspace.HasRead(notice),
            OpenRequested = async id => { if (await _api.GetMissionAsync(id) is { } dto) await OpenMissionAsync(new MissionItem(dto)); },
        };
        Inbox.Items.CollectionChanged += (_, _) => UpdateUnreadContacts();
        Login = login;
        Current = login is null ? home : login;
        IsAuthenticated = login is null;
        home.MissionOpened += mission => _ = OpenMissionAsync(mission);
        home.RecruitRequested += project => _ = OpenWorkspaceAsync(project, rh: true);
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

    public string DataLocation { get; init; } = "";
    public string DataLocationDetail { get; init; } = "";

    public HomeViewModel Home { get; }

    public LoginViewModel? Login { get; }

    /// <summary>Faux en mode local (ADR 0014) : personne ne se connecte, donc personne ne se déconnecte.</summary>
    public bool CanLogout => Login is not null;

    /// <summary>
    /// Toujours : en mode local, les modèles et les clés de l'instance (faute d'espace admin web, ADR 0014) ; sur un serveur,
    /// les clés personnelles de la personne connectée (leçon 134).
    /// </summary>
    public bool CanManageModels => true;

    [RelayCommand]
    private async Task ShowSettingsAsync()
    {
        var screen = new SettingsViewModel(_api, personal: Login is not null);
        Current = screen;
        await screen.LoadAsync();
    }

    [RelayCommand]
    private async Task ShowInstanceModelsAsync()
    {
        if (Login is not null && !IsLlmAdmin)
            return;
        var screen = new SettingsViewModel(_api);
        Current = screen;
        await screen.LoadAsync();
    }

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

    /// <summary>Session perdue (Keycloak l'a fermée) : retour à la connexion, avec la raison. Sur le thread UI.</summary>
    public void SessionExpired()
    {
        if (Login is null || !IsAuthenticated)
            return;
        Logout();
        Login.Error = "Votre session a expiré : reconnectez-vous.";
    }

    [RelayCommand]
    private void Logout()
    {
        Inbox.Clear(); _workspaces.Clear();
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

    // Le panneau est temporaire : quitter un écran ne doit pas le rouvrir au retour sur l'accueil.
    partial void OnCurrentChanging(ObservableObject value) => Home.IsFolderOpen = false;

    public bool IsMissionsSection => Current is HomeViewModel;
    public bool IsOfficeSection => Current is OfficeViewModel or RecruitViewModel or WorkspaceViewModel;
    public bool IsAgentsSection => Current is AgentsViewModel;
    public bool IsReportsSection => Current is ReportsViewModel;

    /// <summary>Une mission s'ouvre dans l'open space de son projet (ADR 0012).</summary>
    public async Task OpenMissionAsync(MissionItem mission)
    {
        if (mission.ProjectId is { } project)
        {
            await OpenWorkspaceAsync(project, mission.Id);
            return;
        }
        var screen = new OfficeViewModel(_api, mission, UserName ?? "vous")
        {
            ProjectName = mission.ProjectId is { } id ? ProjectName(id) : null,
        };
        screen.RecruitRequested += project => _ = RecruitAsync(screen, project);
        screen.RecruitmentOpened += id => _ = OpenRecruitmentAsync(screen, id);
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
            _ = Home.LoadAsync(); // un nouvel agent à soi rejoint aussi la liste des agents
        };
        Current = recruit;
        await recruit.LoadAsync();
    }

    private Task OpenRecruitmentAsync(OfficeViewModel office, Guid id) => OpenKnownMissionAsync(id);

    private async Task OpenKnownMissionAsync(Guid id)
    {
        if (await _api.GetMissionAsync(id) is { } dto) await OpenMissionAsync(new MissionItem(dto));
    }

    public async Task OpenWorkspaceAsync(Guid project, Guid? mission = null, bool rh = false)
    {
        if (Current is WorkspaceViewModel previous) await previous.SaveDraftAsync();
        if (!_workspaces.TryGetValue(project, out var workspace))
        {
            workspace = new WorkspaceViewModel(_api, project, ProjectName(project) ?? "Projet personnel", UserName ?? "Vous");
            workspace.OfficeRequested += item => _ = OpenAnimatedOfficeAsync(item);
            workspace.ProjectChanged += p => { Home.SelectedProject = p; _ = OpenWorkspaceAsync(p.Id); };
            workspace.ReadingChanged += () => _ = Inbox.RefreshAsync();
            workspace.RecruitmentCompleted += () => { _ = Home.LoadAsync(); _ = workspace.LoadAsync(); };
            _workspaces[project] = workspace;
        }
        Current = workspace;
        await workspace.LoadAsync(mission);
        UpdateUnreadContacts();
        if (rh) await workspace.OpenRhCommand.ExecuteAsync(null);
    }

    private async Task OpenAnimatedOfficeAsync(MissionItem mission)
    {
        if (Current is WorkspaceViewModel workspace) await workspace.SaveDraftAsync();
        var office = new OfficeViewModel(_api, mission, UserName ?? "Vous") { ProjectName = mission.ProjectId is { } p ? ProjectName(p) : null };
        office.RecruitRequested += project => _ = OpenWorkspaceAsync(project, rh: true);
        office.RecruitmentOpened += id => _ = OpenKnownMissionAsync(id);
        Current = office;
        await office.LoadAsync();
    }

    public async Task RefreshConversationsAsync()
    {
        if (!IsAuthenticated || _refreshing) return;
        _refreshing = true;
        try
        {
            if (Current is WorkspaceViewModel workspace) await workspace.LoadAsync();
            await Inbox.RefreshAsync();
        }
        finally { _refreshing = false; }
    }

    private void UpdateUnreadContacts()
    {
        foreach (var workspace in _workspaces.Values)
            foreach (var contact in workspace.Contacts)
                contact.IsUnread = false;
    }

    public void ApplyActivity(MissionActivityDto activity)
    {
        if (Current is WorkspaceViewModel workspace) workspace.ApplyActivity(activity);
    }

    private string? ProjectName(Guid project) => Home.Projects.FirstOrDefault(p => p.Id == project) is { IsPersonalDefault: false } p ? p.Name : null;

    /// <summary>« Nouveau collègue » et l'open space ont leur propre barre (maquette) : la navigation se cache.</summary>
    public bool ShowNav => IsAuthenticated && Current is not (RecruitViewModel or OfficeViewModel);

    [RelayCommand]
    private async Task GoHomeAsync()
    {
        if (Current is WorkspaceViewModel workspace) await workspace.SaveDraftAsync();
        Current = Home;
    }

    /// <summary>L'open space : celui de la mission qui attend une décision ou qui tourne, sinon de la plus récente.</summary>
    [RelayCommand]
    private Task OpenOfficeAsync() => Home.SelectedProject is { } project
        ? OpenWorkspaceAsync(project.Id) : Task.CompletedTask;

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
            _ = Home.LoadAsync(); // un nouvel agent à soi rejoint aussi la liste des agents
            back();
        };
        Current = recruit;
        await recruit.LoadAsync();
    }

    [RelayCommand]
    private async Task ShowReportsAsync()
    {
        if (Current is WorkspaceViewModel workspace) await workspace.SaveDraftAsync();
        var screen = new ReportsViewModel(_api);
        screen.ConversationRequested += item => _ = OpenMissionAsync(item);
        Current = screen;
        await screen.LoadAsync();
    }

    /// <summary>Appelé sur le thread UI pour chaque événement du hub.</summary>
    /// <summary>Le texte d'une réponse en train de s'écrire (leçon 120) : à l'écran qui la montre.</summary>
    public void ApplyText(MissionTextDto e)
    {
        if (Current is WorkspaceViewModel workspace)
            workspace.ApplyText(e);
        else if (Current is RecruitViewModel recruit)
            recruit.Rh.ApplyText(e);
        else if (Current is OfficeViewModel office)
            office.ApplyText(e);
        else if (Current is ReportsViewModel reports)
            reports.ApplyText(e);
    }

    public async Task ApplyAsync(MissionEventDto e)
    {
        Home.Apply(e);
        if (Current is WorkspaceViewModel workspace)
            await workspace.ApplyAsync(e);
        else if (Current is OfficeViewModel office)
            await office.ApplyAsync(e);
        else if (Current is ReportsViewModel reports)
            await reports.ApplyAsync(e);
        await Inbox.RefreshAsync();
    }
}
