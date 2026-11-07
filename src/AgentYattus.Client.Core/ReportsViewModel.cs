using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentYattus.Client.Core;

/// <summary>Une bulle du rapport raconté : qui parle, ce qu'il a fait, ce qu'il dit.</summary>
/// <param name="IsYou">Vos bulles s'alignent à droite.</param>
/// <param name="File">Le fichier écrit avec votre accord (REPORT.md), s'il y en a un.</param>
public sealed record ReportMessage(string Name, byte[]? Avatar, string What, string Text, bool IsYou, string? File);

/// <summary>L'écran Rapports : les missions terminées, racontées comme une conversation, et ce qui a été exécuté.</summary>
public sealed partial class ReportsViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<MissionItem> Reports { get; } = [];

    /// <summary>Le rapport choisi, en bulles : votre demande, puis la réponse de l'agent.</summary>
    public ObservableCollection<ReportMessage> Thread { get; } = [];

    [ObservableProperty]
    public partial MissionItem? Selected { get; set; }

    [ObservableProperty]
    public partial ReportStats? Stats { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    private Face? _me;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Reports.Clear();
            foreach (var mission in await api.ListMissionsAsync(cancellationToken: cancellationToken))
                if (mission.Status == MissionStatus.Completed && !string.IsNullOrWhiteSpace(mission.Result))
                    Reports.Add(new MissionItem(mission));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
            return;
        }

        _me = await api.Faces.ManagerAsync(null);
        foreach (var report in Reports.ToList())
            await report.LoadFacesAsync(api.Faces);
        Selected = Reports.FirstOrDefault();
    }

    partial void OnSelectedChanged(MissionItem? value)
    {
        Thread.Clear();
        if (value is not null)
        {
            Thread.Add(new ReportMessage("Vous", _me?.Talk ?? _me?.Work, value.IsReview ? "avez lancé la revue" : "avez posé la question", value.Goal, true, null));
            // La réponse vient du dernier à avoir travaillé : le Rédacteur pour une revue.
            var author = value.Faces.LastOrDefault();
            Thread.Add(new ReportMessage(author?.Name ?? "L'agent", author?.Talk ?? author?.Work, value.IsReview ? "a rédigé le rapport" : "a répondu",
                value.Result ?? "", false, value.IsReview ? "REPORT.md" : null));
        }
        _ = LoadStatsAsync(value);
    }

    // Les chiffres viennent du journal d'audit : ce qui a réellement été exécuté, pas ce que l'agent en dit.
    private async Task LoadStatsAsync(MissionItem? report)
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
