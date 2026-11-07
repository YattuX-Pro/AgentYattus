using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Un membre de l'équipe sur l'accueil : son visage, son poste, et s'il fait la prochaine mission.</summary>
/// <param name="Agent">L'agent qu'il incarne ; null pour le chef d'équipe, qui ne prend pas de mission seul.</param>
public sealed partial class CrewMember(string key, string role, AgentDto? agent) : ObservableObject
{
    public string Key { get; } = key;
    public string Role { get; } = role;
    public AgentDto? Agent { get; } = agent;
    public bool IsLeader => Agent is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Image))]
    public partial Face? Face { get; set; }

    public string Name => Face?.Name ?? Agent?.Name ?? Role;
    public byte[]? Image => Face?.Work;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
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
    [NotifyPropertyChangedFor(nameof(Title), nameof(ChosenLabel))]
    public partial bool WriteReport { get; set; }

    /// <summary>L'équipe du projet choisi, en visages : le chef d'équipe, puis un membre par agent.</summary>
    public ObservableCollection<CrewMember> Crew { get; } = [];

    /// <summary>Qui fera la prochaine mission : l'agent choisi, ou l'Analyste et le Rédacteur pour une revue.</summary>
    public ObservableCollection<CrewMember> Chosen { get; } = [];

    /// <summary>Vous : votre portrait, en haut à droite et dans les rapports.</summary>
    [ObservableProperty]
    public partial Face? Me { get; set; }

    public string Title => WriteReport
        ? $"{NameOf(Scene.Analyst, "L'Analyste")} analyse, {NameOf(Scene.Writer, "le Rédacteur")} rédige."
        : SelectedAgent is { } agent ? $"Quelle mission pour {NameOf(agent.Key, agent.Name)} ?" : "Quelle mission aujourd’hui ?";

    public string ChosenLabel => WriteReport
        ? $"{NameOf(Scene.Analyst, "Analyste")} puis {NameOf(Scene.Writer, "Rédacteur")}"
        : SelectedAgent is { } agent ? NameOf(agent.Key, agent.Name) : "Analyste";

    private string NameOf(string key, string fallback) => Crew.FirstOrDefault(m => m.Key == key)?.Face?.Name ?? fallback;

    [ObservableProperty]
    public partial bool IsBackendConnected { get; set; }

    public ObservableCollection<MissionItem> Missions { get; } = [];

    /// <summary>Les projets où l'on peut lancer une mission (propriétaire ou éditeur). ADR 0009.</summary>
    public ObservableCollection<ProjectDto> Projects { get; } = [];

    /// <summary>Le projet de la prochaine mission ; null : le projet personnel, choisi par le serveur.</summary>
    [ObservableProperty]
    public partial ProjectDto? SelectedProject { get; set; }

    /// <summary>Les agents proposés (ADR 0013) : celui qui fera une mission Question.</summary>
    public ObservableCollection<AgentDto> Agents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ChosenLabel))]
    public partial AgentDto? SelectedAgent { get; set; }

    partial void OnSelectedAgentChanged(AgentDto? value) => SyncChosen();
    partial void OnWriteReportChanged(bool value) => SyncChosen();
    partial void OnSelectedProjectChanged(ProjectDto? value) => _ = LoadCrewAsync();

    /// <summary>Cliquer sur un visage : c'est lui qui fera la mission (une revue garde son circuit).</summary>
    [RelayCommand]
    private void Pick(CrewMember member)
    {
        if (member.Agent is null)
            return;
        WriteReport = false;
        SelectedAgent = Agents.FirstOrDefault(a => a.Key == member.Key) ?? member.Agent;
    }

    private void SyncChosen()
    {
        var keys = WriteReport ? [Scene.Analyst, Scene.Writer] : new[] { SelectedAgent?.Key ?? Scene.Analyst };
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
        if (await faces.LeaderAsync(project) is { } leader)
            crew.Add(new CrewMember(Scene.Leader, "CHEF D’ÉQUIPE", null) { Face = leader });
        foreach (var agent in Agents.ToList())
            crew.Add(new CrewMember(agent.Key, (agent.Name ?? agent.Key).ToUpperInvariant(), agent) { Face = await faces.AgentAsync(project, agent.Key) });
        var me = await faces.ManagerAsync(project);
        // Un autre chargement a commencé entre-temps : le sien gagne.
        if (version != _crewVersion)
            return;
        Crew.Clear();
        foreach (var member in crew)
            Crew.Add(member);
        Me = me;
        SyncChosen();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ChosenLabel));
    }

    public IReadOnlyList<string> Suggestions { get; } = ["Revue de performance", "Audit de sécurité", "Documenter une API"];

    private bool CanLaunchMission => !string.IsNullOrWhiteSpace(Goal);

    [RelayCommand(CanExecute = nameof(CanLaunchMission))]
    private async Task LaunchMissionAsync(CancellationToken cancellationToken)
    {
        Error = null;
        try
        {
            var kind = WriteReport ? MissionKind.Review : MissionKind.Question;
            // Une revue suit son circuit (Analyste puis Rédacteur) ; une question est faite par l'agent choisi.
            var agent = kind == MissionKind.Question && SelectedAgent is { Key: not "analyste" } chosen ? chosen.Key : null;
            var item = new MissionItem(await api.CreateMissionAsync(Goal, cancellationToken, kind, SelectedProject?.Id, agent));
            Missions.Insert(0, item);
            Goal = "";
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
            foreach (var mission in missions)
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
            var selectedAgent = SelectedAgent?.Key;
            Agents.Clear();
            foreach (var agent in await api.GetAgentsAsync(cancellationToken))
                Agents.Add(agent);
            SelectedAgent = Agents.FirstOrDefault(a => a.Key == selectedAgent) ?? Agents.FirstOrDefault(a => a.Key == "analyste") ?? Agents.FirstOrDefault();
        }
        // Sans la liste des agents, la mission part avec l'Analyste.
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Agents.Clear();
            SelectedAgent = null;
        }

        // Les visages en dernier : il faut connaître le projet personnel (celui des missions sans projet).
        api.Faces.DefaultProject = Projects.FirstOrDefault(p => p.IsPersonalDefault)?.Id ?? api.Faces.DefaultProject;
        await LoadCrewAsync();
        foreach (var mission in Missions.ToList())
            await mission.LoadFacesAsync(api.Faces);
    }

    /// <summary>Ouvrir une mission : la navigation est l'affaire de la coquille (<see cref="ShellViewModel"/>).</summary>
    public event Action<MissionItem>? MissionOpened;

    [RelayCommand]
    private void OpenMission(MissionItem mission) => MissionOpened?.Invoke(mission);

    [RelayCommand]
    private void UseSuggestion(string suggestion)
    {
        // Les suggestions sont des revues : elles se terminent par un rapport.
        Goal = suggestion;
        WriteReport = true;
    }

    // Appelé sur le thread UI pour chaque événement du hub.
    public void Apply(MissionEventDto e)
    {
        var mission = Missions.FirstOrDefault(m => m.Id == e.MissionId);
        if (mission is not null)
            mission.Status = e.Status;
    }
}
