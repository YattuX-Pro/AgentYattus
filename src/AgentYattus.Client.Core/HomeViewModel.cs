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

    public IReadOnlyList<string> Suggestions { get; } = ["Revue de performance", "Audit de sécurité", "Documenter une API"];

    private bool CanLaunchMission => !string.IsNullOrWhiteSpace(Goal);

    [RelayCommand(CanExecute = nameof(CanLaunchMission))]
    private async Task LaunchMissionAsync(CancellationToken cancellationToken)
    {
        Error = null;
        try
        {
            var kind = WriteReport ? MissionKind.Review : MissionKind.Question;
            Missions.Insert(0, new MissionItem(await api.CreateMissionAsync(Goal, cancellationToken, kind)));
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
