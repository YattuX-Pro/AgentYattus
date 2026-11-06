using System.Collections.ObjectModel;
using System.Globalization;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Un modèle tel que le sélecteur l'affiche : nom, fournisseur, contexte, prix.</summary>
public sealed record ModelItem(LlmModelDto Model, string ProviderName, bool IsDefault = false)
{
    public string Context => Model.ContextWindow switch
    {
        null => "",
        >= 1_000_000 and var c => (c / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        var c => $"{c / 1000}k",
    };

    /// <summary>Le prix en dollars par million de tokens, entrée · sortie.</summary>
    public string Price => Model.Free ? "gratuit"
        : Model.InputPricePerMillion is { } input && Model.OutputPricePerMillion is { } output
            ? string.Create(CultureInfo.InvariantCulture, $"{input:0.##} $ · {output:0.##} $")
            : "";

    public bool Matches(string query) =>
        query.Length == 0 || $"{Model.Name} {Model.Id} {ProviderName}".Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Paramètres › Modèles, à la manière d'opencode : connecter des fournisseurs (clé d'API, serveur local, adresse
/// compatible OpenAI), puis choisir un modèle dans un sélecteur unique, avec recherche.
/// </summary>
public sealed partial class SettingsViewModel(ApiClient api) : ObservableObject
{
    private readonly List<ModelItem> _allModels = [];

    public ObservableCollection<LlmProviderDto> Providers { get; } = [];
    public ObservableCollection<LlmProviderDto> FilteredProviders { get; } = [];
    public ObservableCollection<LlmConnectionDto> Connections { get; } = [];
    public ObservableCollection<ModelItem> FilteredModels { get; } = [];

    /// <summary>Le modèle par défaut ; null si aucun n'est choisi.</summary>
    [ObservableProperty]
    public partial LlmSelectionDto? Default { get; set; }

    [ObservableProperty]
    public partial ModelItem? DefaultModel { get; set; }

    // Le sélecteur de modèles.
    [ObservableProperty]
    public partial bool IsPickerOpen { get; set; }

    [ObservableProperty]
    public partial string ModelQuery { get; set; } = "";

    // Connecter un fournisseur : d'abord le choisir, puis le formulaire.
    [ObservableProperty]
    public partial bool IsConnectOpen { get; set; }

    [ObservableProperty]
    public partial string ProviderQuery { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsKey), nameof(HasAddress))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial LlmProviderDto? Connecting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string NewLabel { get; set; } = "";

    /// <summary>La clé saisie : effacée dès l'envoi, qu'il réussisse ou non.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string NewKey { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string NewBaseUrl { get; set; } = "";

    /// <summary>La connexion dont la révocation attend une confirmation.</summary>
    [ObservableProperty]
    public partial LlmConnectionDto? PendingRevoke { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? Info { get; set; }

    /// <summary>Un fournisseur en ligne exige une clé ; un serveur local, non.</summary>
    public bool NeedsKey => Connecting?.Kind == LlmConnectionKind.ApiKey;

    public bool HasAddress => Connecting is { Kind: not LlmConnectionKind.ApiKey };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await api.GetLlmStatusAsync(cancellationToken);
            var models = await api.GetLlmModelsAsync(cancellationToken);
            Replace(Providers, status.Providers);
            Replace(Connections, status.Connections);
            var names = status.Connections.ToDictionary(c => c.Id, c => c.ProviderName);
            _allModels.Clear();
            Default = status.Default;
            _allModels.AddRange(models.Models.Select(m => new ModelItem(m, names.GetValueOrDefault(m.ConnectionId, m.Provider),
                IsDefault: m.ConnectionId == Default?.ConnectionId && m.Id == Default?.Model)));
            DefaultModel = _allModels.FirstOrDefault(m => m.IsDefault);
            FilterModels();
            FilterProviders();
            if (models.Errors.Count > 0)
                Error = string.Join(" ", models.Errors.Select(e => e.Message));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }

    partial void OnModelQueryChanged(string value) => FilterModels();

    partial void OnProviderQueryChanged(string value) => FilterProviders();

    private void FilterModels() => Replace(FilteredModels, _allModels.Where(m => m.Matches(ModelQuery.Trim())));

    private void FilterProviders() => Replace(FilteredProviders,
        Providers.Where(p => ProviderQuery.Trim() is var q && (q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase))));

    [RelayCommand]
    private void OpenPicker()
    {
        ModelQuery = "";
        IsPickerOpen = true;
    }

    [RelayCommand]
    private void ClosePicker() => IsPickerOpen = false;

    [RelayCommand]
    private Task ChooseAsync(ModelItem item) => RunAsync(async () =>
    {
        IsPickerOpen = false;
        if (await api.UpdateLlmSettingsAsync(new UpdateLlmSettingsRequest(new LlmSelectionDto(item.Model.ConnectionId, item.Model.Id))) is { } error)
            Error = error;
        else
        {
            await LoadAsync();
            Info = $"{item.Model.Name} sera utilisé à la prochaine mission.";
        }
    });

    [RelayCommand]
    private void OpenConnect()
    {
        ProviderQuery = "";
        Connecting = null;
        IsConnectOpen = true;
    }

    [RelayCommand]
    private void CloseConnect()
    {
        IsConnectOpen = false;
        Connecting = null;
        NewKey = "";
    }

    [RelayCommand]
    private void Pick(LlmProviderDto provider)
    {
        Connecting = provider;
        NewLabel = provider.Name;
        NewKey = "";
        NewBaseUrl = provider.DefaultBaseUrl ?? "";
    }

    private bool CanConnect => Connecting is not null && !string.IsNullOrWhiteSpace(NewLabel)
        && (!NeedsKey || !string.IsNullOrWhiteSpace(NewKey)) && (!HasAddress || !string.IsNullOrWhiteSpace(NewBaseUrl));

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var provider = Connecting!;
        var request = new AddLlmConnectionRequest(provider.Id, NewLabel.Trim(), NewKey.Trim() is { Length: > 0 } key ? key : null,
            HasAddress ? NewBaseUrl.Trim() : null);
        NewKey = "";
        await RunAsync(async () =>
        {
            if (await api.ConnectLlmAsync(request, cancellationToken) is { } error)
                Error = error;
            else
            {
                IsConnectOpen = false;
                Connecting = null;
                await LoadAsync(cancellationToken);
                Info = request.ApiKey is null ? $"{provider.Name} connecté." : $"{provider.Name} connecté. La clé est chiffrée sur le serveur et ne sera plus affichée.";
                // Comme opencode : une fois le fournisseur connecté, on choisit son modèle.
                ModelQuery = provider.Name;
                IsPickerOpen = true;
            }
        });
    }

    [RelayCommand]
    private void AskRevoke(LlmConnectionDto connection) => PendingRevoke = connection;

    [RelayCommand]
    private void CancelRevoke() => PendingRevoke = null;

    [RelayCommand]
    private Task ConfirmRevokeAsync() => RunAsync(async () =>
    {
        if (PendingRevoke is not { } connection)
            return;
        PendingRevoke = null;
        await api.RevokeLlmConnectionAsync(connection.Id);
        await LoadAsync();
        Info = connection.Last4 is null
            ? $"« {connection.Label} » déconnecté."
            : $"« {connection.Label} » révoqué. Pense à révoquer aussi la clé chez {connection.ProviderName} si elle a fuité.";
    });

    private async Task RunAsync(Func<Task> action)
    {
        Error = null;
        Info = null;
        try
        {
            await action();
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
