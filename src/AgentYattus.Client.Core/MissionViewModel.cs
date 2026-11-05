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

    private Guid? _approvalId;

    public bool IsDecisionPending => Mission.IsAwaitingApproval && PendingApproval.Count > 0;

    /// <summary>
    /// On ne peut approuver qu'après avoir vu tout ce qui sera exécuté : un aperçu tronqué ne suffit pas.
    /// </summary>
    public bool CanApprove => IsDecisionPending && (AllShown || PendingApproval.All(item => !item.HasMoreLines));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApprove), nameof(NeedsFullReview))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    public partial bool AllShown { get; set; }

    public bool NeedsFullReview => IsDecisionPending && !CanApprove;

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
                // Une nouvelle demande doit être relue en entier, même si la précédente l'avait été.
                if (dto.PendingApproval?.Id != _approvalId)
                    AllShown = false;
                _approvalId = dto.PendingApproval?.Id;
                PendingApproval.Clear();
                foreach (var call in dto.PendingApproval?.ToolCalls ?? [])
                    PendingApproval.Add(ApprovalItem.From(call));
                OnPropertyChanged(nameof(IsDecisionPending));
                OnPropertyChanged(nameof(CanApprove));
                OnPropertyChanged(nameof(NeedsFullReview));
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

    [RelayCommand(CanExecute = nameof(CanApprove))]
    private Task ApproveAsync(CancellationToken cancellationToken) => DecideAsync(true, cancellationToken);

    [RelayCommand]
    private void ShowAll() => AllShown = true;

    [RelayCommand(CanExecute = nameof(IsDecisionPending))]
    private Task RefuseAsync(CancellationToken cancellationToken) => DecideAsync(false, cancellationToken);

    private async Task DecideAsync(bool approved, CancellationToken cancellationToken)
    {
        try
        {
            if (_approvalId is not { } approvalId)
                return;
            var accepted = await api.DecideAsync(Mission.Id, approvalId, approved, cancellationToken);
            await LoadAsync(cancellationToken);
            // Après le rechargement, qui efface l'erreur précédente.
            if (!accepted)
                Error = "Cette demande n'est plus d'actualité : décision déjà prise ailleurs, ou nouvelle demande à relire.";
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
/// Une action en attente, prête à afficher : « Écrire REPORT.md ? », le chemin complet, le début du contenu
/// et, sur demande, tout le contenu. Les caractères invisibles sont rendus visibles.
/// </summary>
public sealed record ApprovalItem(
    string Tool, string Title, string? Target, IReadOnlyList<string> Preview, IReadOnlyList<string> AllLines, string? Warning)
{
    private const int PreviewLines = 6;

    public int MoreLines => AllLines.Count - Preview.Count;
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
            var lines = (Text("content") ?? "").Split('\n').Select(Visible).ToList();
            return new ApprovalItem(call.Tool, $"Écrire {Visible(Path.GetFileName(path))} ?", Visible(path),
                [.. lines.Take(PreviewLines)], lines, SensitiveLocation(path));
        }

        var raw = Visible(call.Arguments);
        return new ApprovalItem(call.Tool, $"Autoriser {call.Tool} ?", null, [raw], [raw], null);
    }

    // Tout ce qui ne s'imprime pas pourrait faire afficher autre chose que ce qui sera écrit (retour chariot,
    // inversion de sens, caractères invisibles…) : on le remplace par une marque visible. On travaille par Rune
    // (caractère Unicode complet) pour traiter aussi les caractères hors du plan de base, comme le bloc des étiquettes.
    private static string Visible(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\t')
                builder.Append("    ");
            else if (rune.Value == '\r')
                builder.Append('␍');
            else if (rune.Value == ' ' || !IsHidden(rune))
                builder.Append(rune.ToString());
            else
                builder.Append($"⟪U+{rune.Value:X4}⟫");
        }

        return builder.ToString();
    }

    private static bool IsHidden(System.Text.Rune rune) =>
        System.Text.Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.Control
            or System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator
            or System.Globalization.UnicodeCategory.ParagraphSeparator
            or System.Globalization.UnicodeCategory.SpaceSeparator
            or System.Globalization.UnicodeCategory.PrivateUse
            or System.Globalization.UnicodeCategory.OtherNotAssigned
            or System.Globalization.UnicodeCategory.Surrogate
        || rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF // sélecteurs de variante
        // Caractères qui s'affichent vides sans être des espaces (remplissages hangul, braille vide, etc.).
        || rune.Value is 0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or 0x180E or 0x2800 or 0x3164 or 0xFFA0;

    private static string? SensitiveLocation(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => p.StartsWith('.') && p is not "." and not "..")
            ? "Emplacement sensible : fichier ou dossier caché (configuration, .git…)."
            : null;
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
