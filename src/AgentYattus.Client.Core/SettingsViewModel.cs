using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Paramètres › Modèle : le modèle utilisé par les agents, et les clés d'API (ajouter, utiliser, révoquer).</summary>
public sealed partial class SettingsViewModel(ApiClient api) : ObservableObject
{
    private bool _loading;

    public ObservableCollection<LlmModelDto> Models { get; } = [];
    public ObservableCollection<LlmCredentialDto> Credentials { get; } = [];

    [ObservableProperty]
    public partial LlmModelDto? SelectedModel { get; set; }

    [ObservableProperty]
    public partial Guid? ActiveCredentialId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewLabel { get; set; } = "";

    /// <summary>La clé saisie : effacée dès l'envoi, qu'il réussisse ou non.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewKey { get; set; } = "";

    /// <summary>La clé dont la révocation attend une confirmation.</summary>
    [ObservableProperty]
    public partial LlmCredentialDto? PendingRevoke { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? Info { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await api.GetLlmStatusAsync(cancellationToken);
            _loading = true;
            Models.Clear();
            foreach (var model in status.Providers.SelectMany(p => p.Models))
                Models.Add(model);
            Credentials.Clear();
            foreach (var credential in status.Credentials)
                Credentials.Add(credential);
            ActiveCredentialId = status.ActiveCredentialId;
            SelectedModel = Models.FirstOrDefault(m => m.Id == status.Model);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    // Choisir un modèle l'enregistre aussitôt : il servira à la mission suivante.
    partial void OnSelectedModelChanged(LlmModelDto? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(ActiveCredentialId, value.Id, $"Modèle : {value.Name}.");
    }

    private bool CanAdd => !string.IsNullOrWhiteSpace(NewLabel) && !string.IsNullOrWhiteSpace(NewKey);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        var request = new AddLlmCredentialRequest("anthropic", NewLabel.Trim(), NewKey.Trim());
        NewKey = "";
        await RunAsync(async () =>
        {
            if (await api.AddLlmCredentialAsync(request, cancellationToken) is { } error)
                Error = error;
            else
            {
                NewLabel = "";
                await LoadAsync(cancellationToken);
                Info = "Clé ajoutée et chiffrée sur le serveur. Elle ne sera plus jamais affichée.";
            }
        });
    }

    [RelayCommand]
    private Task UseAsync(LlmCredentialDto credential) =>
        SaveAsync(credential.Id, SelectedModel?.Id ?? "", $"Clé « {credential.Label} » utilisée.");

    [RelayCommand]
    private void AskRevoke(LlmCredentialDto credential) => PendingRevoke = credential;

    [RelayCommand]
    private void CancelRevoke() => PendingRevoke = null;

    [RelayCommand]
    private Task ConfirmRevokeAsync() => RunAsync(async () =>
    {
        if (PendingRevoke is not { } credential)
            return;
        PendingRevoke = null;
        await api.RevokeLlmCredentialAsync(credential.Id);
        await LoadAsync();
        Info = $"Clé « {credential.Label} » révoquée. Pense à la révoquer aussi dans la console du fournisseur si elle a fuité.";
    });

    private Task SaveAsync(Guid? credentialId, string model, string done) => RunAsync(async () =>
    {
        if (await api.UpdateLlmSettingsAsync(new UpdateLlmSettingsRequest(credentialId, model)) is { } error)
            Error = error;
        else
        {
            await LoadAsync();
            Info = done;
        }
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
}
