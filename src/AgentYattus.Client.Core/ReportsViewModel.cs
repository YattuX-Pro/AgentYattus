using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentYattus.Client.Core;

/// <summary>L'écran Rapports : les missions terminées, leur réponse, et ce qui a été exécuté pour l'obtenir.</summary>
public sealed partial class ReportsViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<MissionDto> Reports { get; } = [];

    [ObservableProperty]
    public partial MissionDto? Selected { get; set; }

    [ObservableProperty]
    public partial ReportStats? Stats { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Reports.Clear();
            foreach (var mission in await api.ListMissionsAsync(cancellationToken: cancellationToken))
                if (mission.Status == MissionStatus.Completed && !string.IsNullOrWhiteSpace(mission.Result))
                    Reports.Add(mission);
            Selected = Reports.FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }

    // Les chiffres viennent du journal d'audit : ce qui a réellement été exécuté, pas ce que l'agent en dit.
    partial void OnSelectedChanged(MissionDto? value) => _ = LoadStatsAsync(value);

    private async Task LoadStatsAsync(MissionDto? report)
    {
        Stats = null;
        if (report is null)
            return;
        try
        {
            var types = (await api.GetEventsAsync(report.Id)).Select(e => e.Type).ToList();
            if (Selected?.Id == report.Id)
                Stats = new ReportStats(
                    Reads: types.Count(t => t is "outil:read_file" or "outil:list_files"),
                    Approved: types.Count(t => t == "decision:approuvee"),
                    Refused: types.Count(t => t == "decision:refusee"));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Error = ex.Message;
        }
    }
}

public sealed record ReportStats(int Reads, int Approved, int Refused);
