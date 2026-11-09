using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Un collègue recruté, sur l'accueil : son visage, son poste, et s'il fait la prochaine mission.</summary>
/// <param name="key">Le poste qu'il occupe (« analyste »).</param>
/// <param name="agent">L'agent de ce poste ; null s'il n'est plus proposé.</param>
/// <param name="isLeader">Le chef d'équipe (leçon 129), choisi au recrutement : on s'adresse à lui par défaut.</param>
public sealed partial class CrewMember(string key, string role, AgentDto? agent, bool isLeader = false) : ObservableObject
{
    public string Key { get; } = key;
    public string Role { get; } = role;
    public AgentDto? Agent { get; } = agent;
    public bool IsLeader { get; } = isLeader;

    /// <summary>Dans le menu des agents : le chef répond ou répartit, les autres répondent en direct, sans lui.</summary>
    public string Hint => IsLeader ? $"Chef d’équipe · {Agent?.Name ?? Key} · répartit le travail" : $"{Agent?.Name ?? Key} · en direct";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Image))]
    public partial Face? Face { get; set; }

    public string Name => Face?.Name ?? Agent?.Name ?? Role;
    public byte[]? Image => Face?.Work;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    public partial bool IsSelected { get; set; }

    /// <summary>Le chef se met en retrait quand on parle à quelqu'un d'autre.</summary>
    public bool IsDimmed => IsLeader && !IsSelected;

    /// <summary>La bulle sur son visage : en mission (trois points), ou attend votre accord (la cloche).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsWaiting), nameof(BubbleTip))]
    public partial AgentState State { get; set; }

    public bool IsBusy => State == AgentState.Busy;
    public bool IsWaiting => State == AgentState.Waiting;
    public string BubbleTip => IsWaiting ? $"{Name} attend votre accord" : $"{Name} travaille · voir la mission";
}

/// <summary>Une ligne du choix de dossier : un projet, son dossier (« ~/dev/orders-api ») et sa branche git.</summary>
public sealed record ProjectOption(ProjectDto Project, bool IsSelected)
{
    public string Name => Project.Name;
    public bool HasFolder => Project.Workspace is { Length: > 0 };
    public string Sub => HasFolder ? Folders.Short(Project.Workspace!) : "aucun dossier";
    public string? Branch { get; } = Project.Workspace is { Length: > 0 } folder ? Folders.BranchOf(folder) : null;
    public bool IsRepository => Branch is not null;
}

public sealed partial class HomeViewModel(ApiClient api) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchMissionCommand))]
    public partial string Goal { get; set; } = "";

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Vrai : une revue à deux agents, qui se termine par REPORT.md soumis à ton approbation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ChosenLabel), nameof(IsQuestion))]
    public partial bool WriteReport { get; set; }

    /// <summary>
    /// Vrai : une mission Développer (ADR 0016). Le Développeur modifie sur une branche à part, Agent Yattus lance les contrôles
    /// avant et après, le Relecteur relit le diff. Le dossier du projet doit être un dépôt git.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ChosenLabel), nameof(IsQuestion))]
    public partial bool Develop { get; set; }

    /// <summary>Une question : on choisit à qui l'on parle. Une revue ou un développement suivent leur circuit.</summary>
    public bool IsQuestion => !WriteReport && !Develop;

    partial void OnDevelopChanged(bool value)
    {
        if (value)
            WriteReport = false;
        SyncChosen();
    }

    // ── Les compétences choisies pour la mission (leçon 132) ──

    /// <summary>Les compétences que l'on peut choisir (approuvées, de l'instance ou à soi) : chargées en entier dès le départ.</summary>
    public ObservableCollection<SkillChoice> MissionSkills { get; } = [];

    public string SkillsLabel => MissionSkills.Count(s => s.IsSelected) is var n and > 0 ? $"Compétences · {n}" : "Compétences";

    /// <summary>Un SKILL.md importé, en brouillon : on le relit en entier avant de le valider.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingSkill), nameof(PendingSkillText))]
    public partial SkillImportDto? PendingSkill { get; set; }

    public bool HasPendingSkill => PendingSkill is not null;

    /// <summary>Tout ce qu'on valide : le nom, quand s'en servir, la procédure entière, ses références, ce qui mérite attention, et qui la verra.</summary>
    public string PendingSkillText => PendingSkill is not { } p ? "" :
        $"{p.Skill.Name} — {p.Skill.Description}\n\n{p.Skill.Body}" +
        (p.Skill.Sources is { Count: > 0 } sources ? "\n\nRéférences :\n" + string.Join('\n', sources.Select(r => $"- {r.Kind} : {r.Location}")) : "") +
        (p.Warnings.Count > 0 ? "\n\nÀ relire :\n" + string.Join('\n', p.Warnings.Select(w => $"- {w}")) : "") +
        "\n\nPortée : personnelle, visible de vous seul. Une compétence ne donne aucun droit à l'agent.";

    private void AddSkill(SkillDto skill, bool selected)
    {
        var choice = new SkillChoice(skill) { IsSelected = selected };
        choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SkillsLabel));
        MissionSkills.Add(choice);
        OnPropertyChanged(nameof(SkillsLabel));
    }

    /// <summary>Importer un SKILL.md : son texte seulement (rien n'est exécuté). Il arrive en brouillon, à relire.</summary>
    [RelayCommand]
    private async Task ImportSkillAsync()
    {
        if (PickFiles is null || (await PickFiles()).FirstOrDefault() is not { } path)
            return;
        try
        {
            var file = new FileInfo(path);
            if (file.Length > 64 * 1024)
                throw new IOException("Un SKILL.md de 64 Ko au plus.");
            PendingSkill = await api.ImportSkillAsync(await File.ReadAllTextAsync(path));
            Error = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    /// <summary>Valider après relecture : elle devient utilisable, et choisie pour la prochaine mission.</summary>
    [RelayCommand]
    private async Task ValidateSkillAsync()
    {
        if (PendingSkill is not { } pending)
            return;
        try
        {
            await api.SetSkillStatusAsync(pending.Skill.Key, SkillStatus.Approved);
            AddSkill(pending.Skill with { Status = SkillStatus.Approved }, selected: true);
            PendingSkill = null;
            Error = null;
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
        }
    }

    /// <summary>Ne pas la garder : le brouillon est retiré.</summary>
    [RelayCommand]
    private async Task DiscardSkillAsync()
    {
        if (PendingSkill is not { } pending)
            return;
        try
        {
            await api.DeleteSkillAsync(pending.Skill.Key);
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
        }
        PendingSkill = null;
    }

    /// <summary>L'équipe du projet choisi, en visages : seulement les collègues recrutés, le chef d'équipe en premier.</summary>
    public ObservableCollection<CrewMember> Crew { get; } = [];

    /// <summary>Qui reçoit une question quand on ne choisit personne : le chef d'équipe, sinon le premier collègue (leçon 129).</summary>
    private CrewMember? Default => Crew.FirstOrDefault(m => m.IsLeader) ?? Crew.FirstOrDefault();

    /// <summary>Qui fera la prochaine mission : le chef, l'agent choisi, ou l'Analyste et le Rédacteur pour une revue.</summary>
    public ObservableCollection<CrewMember> Chosen { get; } = [];

    /// <summary>Vous : votre portrait, en haut à droite et dans les rapports.</summary>
    [ObservableProperty]
    public partial Face? Me { get; set; }

    public string Title => Develop
        ? $"{NameOf(Scene.Developer, "Le Développeur")} développe à part, {NameOf(Scene.Reviewer, "le Relecteur")} relit."
        : WriteReport
        ? $"{NameOf(Scene.Analyst, "L'Analyste")} analyse, {NameOf(Scene.Writer, "le Rédacteur")} rédige."
        : SelectedAgent is { } agent ? $"Quelle mission pour {NameOf(agent.Key, agent.Name)} ?"
        : Default is { IsLeader: false } first ? $"Quelle mission pour {first.Name} ?"
        : "Quelle mission pour l’équipe ?";

    public string ChosenLabel => Develop
        ? $"{NameOf(Scene.Developer, "Développeur")} puis {NameOf(Scene.Reviewer, "Relecteur")}"
        : WriteReport
        ? $"{NameOf(Scene.Analyst, "Analyste")} puis {NameOf(Scene.Writer, "Rédacteur")}"
        : SelectedAgent is { } agent ? NameOf(agent.Key, agent.Name) : Default?.Name ?? "L’Analyste";

    private string NameOf(string key, string fallback) => Crew.FirstOrDefault(m => m.Key == key)?.Face?.Name ?? fallback;

    [ObservableProperty]
    public partial bool IsBackendConnected { get; set; }

    public ObservableCollection<MissionItem> Missions { get; } = [];

    /// <summary>Les projets où l'on peut lancer une mission (propriétaire ou éditeur). ADR 0009.</summary>
    public ObservableCollection<ProjectDto> Projects { get; } = [];

    /// <summary>Le projet de la prochaine mission ; null : le projet personnel, choisi par le serveur.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderName), nameof(HasFolder), nameof(NoFolder), nameof(FolderPath), nameof(Branch),
        nameof(ShowBranch), nameof(BranchLabel), nameof(CanChooseFolder))]
    public partial ProjectDto? SelectedProject { get; set; }

    /// <summary>
    /// Les agents proposés (ADR 0013). Aucun choisi : on parle au chef d'équipe, qui répond ou confie la demande au bon
    /// collègue ; un autre collègue choisi répond en direct, sans le chef (leçons 128 et 129).
    /// </summary>
    public ObservableCollection<AgentDto> Agents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ChosenLabel))]
    public partial AgentDto? SelectedAgent { get; set; }

    partial void OnSelectedAgentChanged(AgentDto? value) => SyncChosen();
    partial void OnWriteReportChanged(bool value)
    {
        if (value)
            Develop = false;
        SyncChosen();
    }
    partial void OnSelectedProjectChanged(ProjectDto? value)
    {
        OnBranch = false;
        RefreshProjectOptions();
        _ = LoadCrewAsync();
    }

    /// <summary>Vrai : la mission travaille sur une branche git à part (leçon 127).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchLabel))]
    public partial bool OnBranch { get; set; }

    /// <summary>Les fichiers joints à la prochaine mission, déjà copiés dans le dossier du projet (leçon 115).</summary>
    public ObservableCollection<AttachmentDto> Attachments { get; } = [];

    /// <summary>Ouvre le sélecteur de fichiers du système ; fourni par la vue.</summary>
    public Func<Task<IReadOnlyList<string>>>? PickFiles { get; set; }

    [RelayCommand]
    private async Task AttachAsync()
    {
        if (PickFiles is not null)
            await AttachFilesAsync(await PickFiles());
    }

    /// <summary>Joindre des fichiers (bouton ou glisser-déposer) : chacun est copié tout de suite dans le dossier du projet.</summary>
    public async Task AttachFilesAsync(IEnumerable<string> paths)
    {
        if (SelectedProject is not { } project)
            return;
        foreach (var path in paths)
        {
            try
            {
                Attachments.Add(await api.UploadAttachmentAsync(project.Id, path));
                Error = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                Error = ex.Message;
            }
        }
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentDto attachment) => Attachments.Remove(attachment);

    /// <summary>Le but envoyé : celui écrit, puis la liste des pièces jointes, que l'agent lit avec read_file.</summary>
    internal string GoalWithAttachments() => Attachments.Count == 0
        ? Goal
        : $"{Goal.TrimEnd()}\n\nPièces jointes (dans le dossier du projet, à lire avec read_file) :\n"
          + string.Join('\n', Attachments.Select(a => $"- {a.Path}"));

    // ── Le dossier de travail (leçon 128) : une pastille sous le compositeur, un panneau dans l'application ──

    // Où l'on peut choisir le dossier de ses projets (ADR 0014) ; inconnu tant que l'API n'a pas répondu.
    private WorkspaceRootsDto _roots = new(false, []);

    /// <summary>La pastille : le nom du projet ; en pointillés tant qu'il n'a pas de dossier.</summary>
    public string FolderName => SelectedProject?.Name ?? "Projet personnel";

    public bool HasFolder => SelectedProject?.Workspace is { Length: > 0 };
    public bool NoFolder => !HasFolder;

    /// <summary>Le chemin complet, en info-bulle de la pastille.</summary>
    public string? FolderPath => SelectedProject?.Workspace;

    /// <summary>La branche courante du dossier, lue sur cet ordinateur ; null hors dépôt git.</summary>
    public string? Branch => SelectedProject?.Workspace is { Length: > 0 } folder ? Folders.BranchOf(folder) : null;

    /// <summary>
    /// La pastille de branche : pour un dépôt git, ou pour un dossier du serveur qu'on ne voit pas d'ici
    /// (le serveur refusera le mode branche si ce n'est pas un dépôt).
    /// </summary>
    public bool ShowBranch => SelectedProject?.Workspace is { Length: > 0 } folder && (Branch is not null || !Directory.Exists(folder));

    public string BranchLabel => OnBranch ? "branche à part" : Branch ?? "branche";

    /// <summary>Le gestionnaire du projet, s'il a un endroit où choisir (partout, ou sous un dossier autorisé).</summary>
    public bool CanChooseFolder => SelectedProject?.CanManage == true && (_roots.Anywhere || _roots.Roots.Count > 0);

    /// <summary>Le panneau du dossier, ouvert sous la pastille.</summary>
    [ObservableProperty]
    public partial bool IsFolderOpen { get; set; }

    partial void OnIsFolderOpenChanged(bool value)
    {
        if (!value)
            IsBrowsing = false;
    }

    /// <summary>Chercher un projet par son nom ou son dossier.</summary>
    [ObservableProperty]
    public partial string ProjectFilter { get; set; } = "";

    partial void OnProjectFilterChanged(string value) => RefreshProjectOptions();

    public ObservableCollection<ProjectOption> ProjectOptions { get; } = [];

    private void RefreshProjectOptions()
    {
        var filter = ProjectFilter.Trim();
        ProjectOptions.Clear();
        foreach (var project in Projects.Where(p => filter.Length == 0
                     || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                     || (p.Workspace?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false)))
            ProjectOptions.Add(new ProjectOption(project, project.Id == SelectedProject?.Id));
    }

    [RelayCommand]
    private void PickProject(ProjectOption option)
    {
        SelectedProject = option.Project;
        IsFolderOpen = false;
    }

    [RelayCommand]
    private void ToggleBranch() => OnBranch = !OnBranch;

    /// <summary>Parcourir les dossiers dans le panneau, sans la boîte de dialogue du système.</summary>
    [ObservableProperty]
    public partial bool IsBrowsing { get; set; }

    /// <summary>Le dossier affiché ; null : la liste des dossiers autorisés (quand il y en a plusieurs).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CrumbHead), nameof(CrumbTail))]
    public partial string? BrowsePath { get; set; }

    /// <summary>Le fil d'Ariane : « ~ / » puis « dev », le dossier courant en clair.</summary>
    public string CrumbHead => BrowsePath is null || Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(BrowsePath)) is not { } parent
        ? "" : Folders.Short(parent) + " /";

    public string CrumbTail => BrowsePath is null ? "Dossiers autorisés" : Folders.Short(BrowsePath) == "~" ? "~" : Folders.Name(BrowsePath);

    public ObservableCollection<FolderEntry> Entries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    public partial FolderEntry? SelectedEntry { get; set; }

    private IReadOnlyList<string> BrowseRoots => _roots.Anywhere ? [Folders.UserHome] : _roots.Roots;

    private bool Allowed(string path) => _roots.Anywhere || Folders.Within(path, _roots.Roots);

    [RelayCommand]
    private void Browse()
    {
        // On part d'à côté du dossier actuel, sinon du dossier autorisé (ou du dossier personnel).
        var parent = SelectedProject?.Workspace is { Length: > 0 } current && Directory.Exists(current)
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) : null;
        Show(parent is not null && Allowed(parent) ? parent : BrowseRoots.Count == 1 ? BrowseRoots[0] : null);
        IsBrowsing = true;
    }

    private void Show(string? folder)
    {
        BrowsePath = folder;
        SelectedEntry = null;
        Entries.Clear();
        foreach (var path in folder is null ? BrowseRoots : Folders.Children(folder))
            Entries.Add(new FolderEntry(path));
    }

    [RelayCommand]
    private void Enter(FolderEntry entry) => Show(entry.Path);

    [RelayCommand]
    private void Select(FolderEntry entry)
    {
        foreach (var other in Entries)
            other.IsSelected = other == entry;
        SelectedEntry = entry;
    }

    /// <summary>Remonter d'un dossier ; au-dessus de ce qui est permis, on revient à la liste des projets.</summary>
    [RelayCommand]
    private void Up()
    {
        var parent = BrowsePath is null ? null : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(BrowsePath));
        if (parent is not null && Allowed(parent))
            Show(parent);
        else if (BrowsePath is not null && BrowseRoots.Count > 1)
            Show(null);
        else
            IsBrowsing = false;
    }

    [RelayCommand]
    private void CancelBrowse() => IsBrowsing = false;

    private bool CanChoose => SelectedEntry is not null || BrowsePath is not null;

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private Task ChooseAsync() => (SelectedEntry?.Path ?? BrowsePath) is { } path ? SetFolderAsync(path) : Task.CompletedTask;

    /// <summary>Le dossier glissé au-dessus de la fenêtre, pas encore lâché ; null sinon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraggingFolder), nameof(DragLabel), nameof(DragName), nameof(DragBranch))]
    public partial string? DraggedFolder { get; set; }

    public bool IsDraggingFolder => DraggedFolder is not null;
    public string DragLabel => DraggedFolder is null ? "" : Folders.Short(DraggedFolder);
    public string DragName => DraggedFolder is null ? "" : Folders.Name(DraggedFolder);
    public string? DragBranch => DraggedFolder is null ? null : Folders.BranchOf(DraggedFolder);

    /// <summary>Le dossier du projet choisi : déposé sur la fenêtre, ou choisi dans le panneau. Le serveur le vérifie (ADR 0014).</summary>
    public async Task SetFolderAsync(string path)
    {
        if (SelectedProject is not { } project || !CanChooseFolder)
            return;
        try
        {
            await api.SetWorkspaceAsync(project.Id, path);
            var updated = project with { Workspace = Path.GetFullPath(path) };
            Projects[Projects.IndexOf(project)] = updated;
            SelectedProject = updated;
            Error = null;
            IsFolderOpen = false;
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
        }
    }

    // ── L'équipe ──

    /// <summary>Cliquer sur un visage : le chef répond ou répartit, un collègue répond en direct (une revue garde son circuit).</summary>
    [RelayCommand]
    private void Pick(CrewMember member)
    {
        WriteReport = false;
        Develop = false;
        SelectedAgent = member.IsLeader ? null : member.Agent;
    }

    private void SyncChosen()
    {
        var keys = Develop ? [Scene.Developer, Scene.Reviewer]
            : WriteReport ? [Scene.Analyst, Scene.Writer]
            : (SelectedAgent?.Key ?? Default?.Key) is { } chosen ? [chosen] : Array.Empty<string>();
        foreach (var member in Crew)
            member.IsSelected = keys.Contains(member.Key);
        Chosen.Clear();
        foreach (var key in keys)
            if (Crew.FirstOrDefault(m => m.Key == key) is { } member)
                Chosen.Add(member);
    }

    private int _crewVersion;

    /// <summary>Recharge l'équipe du projet choisi (après un changement de projet ou un recrutement).</summary>
    public async Task LoadCrewAsync()
    {
        var version = ++_crewVersion;
        var faces = api.Faces;
        faces.DefaultProject ??= Projects.FirstOrDefault(p => p.IsPersonalDefault)?.Id;
        var project = SelectedProject?.Id;
        var crew = new List<CrewMember>();
        var team = await faces.TeamOfAsync(project);
        // Seulement les collègues recrutés (leçon 129) : le chef d'abord, puis dans l'ordre des postes.
        var agents = Agents.ToList();
        foreach (var colleague in (team?.Colleagues ?? []).Where(c => c.Active)
                     .OrderByDescending(c => c.Leader).ThenBy(c => agents.FindIndex(a => a.Key == c.Role) is var i and >= 0 ? i : int.MaxValue))
        {
            var agent = agents.FirstOrDefault(a => a.Key == colleague.Role);
            var role = colleague.Leader ? "CHEF D’ÉQUIPE" : (agent?.Name ?? MissionItem.Label(colleague.Role)).ToUpperInvariant();
            crew.Add(new CrewMember(colleague.Role, role, agent, colleague.Leader) { Face = await faces.AgentAsync(project, colleague.Role) });
        }
        var me = await faces.ManagerAsync(project);
        // Un autre chargement a commencé entre-temps : le sien gagne.
        if (version != _crewVersion)
            return;
        Crew.Clear();
        foreach (var member in crew)
            Crew.Add(member);
        Me = me;
        CanRecruit = team?.CanRecruit == true;
        RefreshStates();
        SyncChosen();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ChosenLabel));
    }

    /// <summary>Le cercle « Recruter » au bout de l'équipe, pour le propriétaire et les éditeurs du projet.</summary>
    [ObservableProperty]
    public partial bool CanRecruit { get; set; }

    /// <summary>« Recruter » : la coquille ouvre « Nouveau collègue » pour ce projet.</summary>
    public event Action<Guid>? RecruitRequested;

    [RelayCommand]
    private void Recruit()
    {
        if ((SelectedProject?.Id ?? api.Faces.DefaultProject) is { } project)
            RecruitRequested?.Invoke(project);
    }

    // ── Les missions : des bulles sur les visages et un panneau en haut à droite, plus de grille sous le compositeur ──

    /// <summary>Le panneau des missions : celles qui attendent votre accord, puis celles en cours, puis les plus récentes.</summary>
    public ObservableCollection<MissionItem> Activity { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunning), nameof(IsQuiet))]
    public partial int RunningCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaiting), nameof(IsQuiet))]
    public partial int WaitingCount { get; set; }

    public bool HasRunning => RunningCount > 0;
    public bool HasWaiting => WaitingCount > 0;

    /// <summary>Rien en cours ni en attente : la pastille montre seulement l'historique.</summary>
    public bool IsQuiet => RunningCount == 0 && WaitingCount == 0;

    public string ActivityTip => $"Missions : {RunningCount} en cours, {WaitingCount} attend{(WaitingCount > 1 ? "ent" : "")} votre accord";

    /// <summary>Qui est en mission, qui attend votre accord : d'après les missions récentes.</summary>
    private void RefreshStates()
    {
        foreach (var member in Crew)
        {
            var mine = Missions.Where(m => m.Roles.Contains(member.Key)).ToList();
            // Dans une revue, c'est le dernier du circuit (le Rédacteur) qui attend votre accord.
            member.State = mine.Any(m => m.IsAwaitingApproval && m.Roles[^1] == member.Key) ? AgentState.Waiting
                : mine.Any(m => m.IsActive) ? AgentState.Busy
                : AgentState.Free;
        }
        RunningCount = Missions.Count(m => m.IsActive);
        WaitingCount = Missions.Count(m => m.IsAwaitingApproval);
        OnPropertyChanged(nameof(ActivityTip));
        Activity.Clear();
        foreach (var mission in Missions.OrderBy(m => m.IsAwaitingApproval ? 0 : m.IsActive ? 1 : 2).ThenByDescending(m => m.CreatedAt).Take(5))
            Activity.Add(mission);
    }

    /// <summary>La bulle d'un visage : la mission qui attend son accord, sinon celle où il travaille.</summary>
    [RelayCommand]
    private void OpenMemberMission(CrewMember member)
    {
        var mine = Missions.Where(m => m.Roles.Contains(member.Key)).ToList();
        if ((mine.FirstOrDefault(m => m.IsAwaitingApproval) ?? mine.FirstOrDefault(m => m.IsActive)) is { } mission)
            MissionOpened?.Invoke(mission);
    }

    private bool CanLaunchMission => !string.IsNullOrWhiteSpace(Goal);

    [RelayCommand(CanExecute = nameof(CanLaunchMission))]
    private async Task LaunchMissionAsync(CancellationToken cancellationToken)
    {
        Error = null;
        try
        {
            var kind = Develop ? MissionKind.Develop : WriteReport ? MissionKind.Review : MissionKind.Question;
            // Une revue suit son circuit (Analyste puis Rédacteur) ; une question va à l'agent choisi, sinon au chef d'équipe
            // (ou, sans chef, au premier collègue ; sans équipe, le serveur la confie à l'Analyste).
            var agent = kind == MissionKind.Question ? SelectedAgent?.Key ?? Default?.Key : null;
            var skills = MissionSkills.Where(s => s.IsSelected).Select(s => s.Skill.Key).ToList();
            var item = new MissionItem(await api.CreateMissionAsync(GoalWithAttachments(), cancellationToken, kind, SelectedProject?.Id, agent, OnBranch,
                skills.Count == 0 ? null : skills));
            Attachments.Clear();
            Missions.Insert(0, item);
            RefreshStates();
            Goal = "";
            MissionOpened?.Invoke(item);
            await item.LoadFacesAsync(api.Faces);
        }
        // Délai dépassé ou réponse illisible ne doivent pas faire planter l'interface.
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }

    /// <summary>Charge les missions récentes, y compris celles lancées depuis un autre client.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var missions = await api.ListMissionsAsync(cancellationToken: cancellationToken);
            Missions.Clear();
            foreach (var mission in missions.Where(m => m.Status != MissionStatus.Failed))
                Missions.Add(new MissionItem(mission));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
            return;
        }

        try
        {
            // Un lecteur voit les missions d'un projet mais n'y lance rien : on ne le propose pas.
            var selected = SelectedProject?.Id;
            Projects.Clear();
            foreach (var project in (await api.GetProjectsAsync(cancellationToken)).Where(p => p.MyAccess is ProjectRole.Owner or ProjectRole.Editor))
                Projects.Add(project);
            SelectedProject = Projects.FirstOrDefault(p => p.Id == selected)
                ?? Projects.FirstOrDefault(p => p.IsPersonalDefault)
                ?? Projects.FirstOrDefault();
            RefreshProjectOptions();
        }
        // Sans la liste des projets (API plus ancienne, coupure), on lance quand même : dans le projet personnel.
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Projects.Clear();
            SelectedProject = null;
        }

        try
        {
            _roots = await api.GetWorkspaceRootsAsync(cancellationToken);
            OnPropertyChanged(nameof(CanChooseFolder));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // Sans la réponse, on ne propose pas de choisir ; le reste de l'accueil fonctionne.
        }

        try
        {
            // Aucun agent choisi par défaut : on parle au chef d'équipe (leçons 128 et 129).
            var selectedAgent = SelectedAgent?.Key;
            Agents.Clear();
            foreach (var agent in await api.GetAgentsAsync(cancellationToken))
                Agents.Add(agent);
            SelectedAgent = Agents.FirstOrDefault(a => a.Key == selectedAgent);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Agents.Clear();
            SelectedAgent = null;
        }

        try
        {
            // Les compétences que l'on peut choisir : approuvées ou dépréciées ; celles déjà cochées le restent.
            var selected = MissionSkills.Where(s => s.IsSelected).Select(s => s.Skill.Key).ToHashSet();
            MissionSkills.Clear();
            foreach (var skill in (await api.GetSkillsAsync(cancellationToken)).Where(s => s.Status != SkillStatus.Draft))
                AddSkill(skill, selected.Contains(skill.Key));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // Sans la liste, on lance sans compétence choisie.
        }

        // Les visages en dernier : il faut connaître le projet personnel (celui des missions sans projet).
        api.Faces.DefaultProject = Projects.FirstOrDefault(p => p.IsPersonalDefault)?.Id ?? api.Faces.DefaultProject;
        await LoadCrewAsync();
        foreach (var mission in Missions.ToList())
            await mission.LoadFacesAsync(api.Faces);
        RefreshStates();
    }

    /// <summary>Ouvrir une mission : la navigation est l'affaire de la coquille (<see cref="ShellViewModel"/>).</summary>
    public event Action<MissionItem>? MissionOpened;

    [RelayCommand]
    private void OpenMission(MissionItem mission) => MissionOpened?.Invoke(mission);

    // Appelé sur le thread UI pour chaque événement du hub.
    public void Apply(MissionEventDto e)
    {
        var mission = Missions.FirstOrDefault(m => m.Id == e.MissionId);
        if (mission is not null)
        {
            mission.Status = e.Status;
            if (e.Status == MissionStatus.Failed)
                Missions.Remove(mission);
        }
        RefreshStates();
    }
}
