using System.Collections.ObjectModel;
using System.Text.Json;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>L'écran « Mission en direct » : où en est la mission, ce que l'agent a fait, ce qu'il attend.</summary>
public sealed partial class MissionViewModel(ApiClient api, MissionItem mission) : ObservableObject
{
    public MissionItem Mission { get; } = mission;

    [ObservableProperty]
    public partial string? Result { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Le journal complet, du plus ancien au plus récent.</summary>
    public ObservableCollection<TimelineEntry> Journal { get; } = [];

    /// <summary>Les actions qui attendent ta décision : exactement ce qui sera exécuté si tu approuves.</summary>
    public ObservableCollection<ApprovalItem> PendingApproval { get; } = [];

    public bool IsDecisionPending => Mission.IsAwaitingApproval && PendingApproval.Count > 0;

    /// <summary>Les outils utilisés par l'agent, avec leur nombre d'appels : « read_file 3 ».</summary>
    public ObservableCollection<ToolUsage> Tools { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApproved), nameof(IsRefused))]
    public partial bool? Decision { get; set; }

    public bool IsApproved => Decision == true;
    public bool IsRefused => Decision == false;

    /// <summary>Faux tant qu'aucune décision humaine n'a été demandée : le nœud « Vous » reste éteint.</summary>
    [ObservableProperty]
    public partial bool HumanInvolved { get; set; }

    public TimelineEntry? LastEntry => Journal.Count > 0 ? Journal[^1] : null;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await api.GetMissionAsync(Mission.Id, cancellationToken) is { } dto)
            {
                Mission.Status = dto.Status;
                Result = dto.Result;
                PendingApproval.Clear();
                foreach (var call in dto.PendingApproval?.ToolCalls ?? [])
                    PendingApproval.Add(ApprovalItem.From(call));
                OnPropertyChanged(nameof(IsDecisionPending));
                ApproveCommand.NotifyCanExecuteChanged();
                RefuseCommand.NotifyCanExecuteChanged();
            }

            var events = await api.GetEventsAsync(Mission.Id, cancellationToken);
            Journal.Clear();
            foreach (var e in events)
                Journal.Add(TimelineEntry.From(e));

            Tools.Clear();
            foreach (var group in events.Select(e => e.Type).Where(t => t.StartsWith("outil:", StringComparison.Ordinal))
                         .GroupBy(t => t["outil:".Length..]))
                Tools.Add(new ToolUsage(group.Key, group.Count()));

            // La dernière décision prise, s'il y en a une.
            Decision = events.LastOrDefault(e => e.Type.StartsWith("decision:", StringComparison.Ordinal)) switch
            {
                { Type: "decision:approuvee" } => true,
                { Type: "decision:refusee" } => false,
                _ => null,
            };
            HumanInvolved = Mission.IsAwaitingApproval || Decision is not null;
            OnPropertyChanged(nameof(LastEntry));
            Error = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDecisionPending))]
    private Task ApproveAsync(CancellationToken cancellationToken) => DecideAsync(true, cancellationToken);

    [RelayCommand(CanExecute = nameof(IsDecisionPending))]
    private Task RefuseAsync(CancellationToken cancellationToken) => DecideAsync(false, cancellationToken);

    private async Task DecideAsync(bool approved, CancellationToken cancellationToken)
    {
        try
        {
            var accepted = await api.DecideAsync(Mission.Id, approved, cancellationToken);
            await LoadAsync(cancellationToken);
            // Après le rechargement, qui efface l'erreur précédente.
            if (!accepted)
                Error = "Cette décision a déjà été prise, peut-être depuis un autre appareil.";
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }

    /// <summary>Un événement du hub : on recharge si c'est notre mission.</summary>
    // ponytail: rechargement à chaque changement de statut ; les appels d'outils pendant un même tour
    // apparaissent à la fin de ce tour. Pousser chaque événement d'audit par SignalR si le direct doit être plus fin.
    public Task ApplyAsync(MissionEventDto e) => e.MissionId == Mission.Id ? LoadAsync() : Task.CompletedTask;
}

public sealed record ToolUsage(string Name, int Count);

/// <summary>
/// Une action en attente, prête à afficher : « Écrire REPORT.md ? », les premières lignes du contenu,
/// et le nombre de lignes restantes.
/// </summary>
public sealed record ApprovalItem(string Tool, string Title, string? Target, IReadOnlyList<string> Preview, int MoreLines)
{
    private const int PreviewLines = 6;

    public bool HasMoreLines => MoreLines > 0;

    public static ApprovalItem From(ToolCallDto call)
    {
        Dictionary<string, JsonElement>? arguments = null;
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.Arguments);
        }
        catch (JsonException)
        {
            // Arguments illisibles : on montre l'appel brut plutôt que rien.
        }

        string? Text(string name) =>
            arguments is not null && arguments.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        if (call.Tool == "write_file" && Text("path") is { } path)
        {
            var lines = (Text("content") ?? "").Split('\n');
            return new ApprovalItem(call.Tool, $"Écrire {Path.GetFileName(path)} ?", path,
                [.. lines.Take(PreviewLines)], Math.Max(0, lines.Length - PreviewLines));
        }

        return new ApprovalItem(call.Tool, $"Autoriser {call.Tool} ?", null, [call.Arguments], 0);
    }
}

/// <summary>Une entrée du journal, avec le genre de marque à dessiner sur la frise.</summary>
public sealed record TimelineEntry(string Label, DateTimeOffset At, bool IsTool, bool IsDecision, bool IsFailure)
{
    public bool IsStatus => !IsTool && !IsDecision && !IsFailure;

    public static TimelineEntry From(AuditEntryDto e) => e.Type switch
    {
        _ when e.Type.StartsWith("outil:", StringComparison.Ordinal) => new(e.Type["outil:".Length..], e.At, true, false, false),
        "decision:approuvee" => new("Écriture approuvée", e.At, false, true, false),
        "decision:refusee" => new("Écriture refusée", e.At, false, true, true),
        _ when e.Type.StartsWith("approbation-demandee:", StringComparison.Ordinal) => new("Approbation demandée", e.At, false, true, false),
        "limite-de-tours" => new("Limite de tours atteinte", e.At, false, false, true),
        "Running" => new("En cours", e.At, false, false, false),
        "AwaitingApproval" => new("En attente de décision", e.At, false, false, false),
        "Completed" => new("Terminée", e.At, false, false, false),
        "Failed" => new("Échec", e.At, false, false, true),
        _ => new(e.Type, e.At, false, false, false),
    };
}
