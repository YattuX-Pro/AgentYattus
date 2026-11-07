using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

public sealed partial class HomeViewModel(ApiClient api) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchMissionCommand))]
    public partial string Goal { get; set; } = "";

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Vrai : une revue à deux agents, qui se termine par REPORT.md soumis à ton approbation.</summary>
    [ObservableProperty]
    public partial bool WriteReport { get; set; }

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
    public partial AgentDto? SelectedAgent { get; set; }

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
            Missions.Insert(0, new MissionItem(await api.CreateMissionAsync(Goal, cancellationToken, kind, SelectedProject?.Id, agent)));
            Goal = "";
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
