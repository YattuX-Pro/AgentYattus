using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

public sealed partial class RecruitmentMessage(bool fromUser, string text) : ObservableObject
{
    public bool FromUser { get; } = fromUser;
    [ObservableProperty]
    public partial string Text { get; set; } = text;
    public string Speaker => FromUser ? "Vous" : "Agent RH";
}

/// <summary>Un entretien RH est une mission : échanges persistés, droits du projet et budgets habituels.</summary>
public sealed partial class RecruitmentViewModel(ApiClient api, Guid projectId) : ObservableObject
{
    private Guid? _mission;
    private bool _waiting;
    private bool _submitting;
    private RecruitmentMessage? _streamingMessage;
    private readonly List<MissionTextDto> _earlyText = [];
    private int _earlyChars;
    public ObservableCollection<RecruitmentMessage> Messages { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(CreateCommand), nameof(ApplyCommand), nameof(ResumeCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(CanResume), nameof(CanStop))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(HasDraft), nameof(ToolsLabel))]
    public partial RecruitmentDraftDto? Draft { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? Info { get; set; }

    public bool HasDraft => Draft is not null;
    public string ToolsLabel => Draft is null ? "" : string.Join(" · ", Draft.Tools.Select(name => new ToolRow(new ToolInfoDto(name, name, false)).Label));
    public event Action<RecruitmentDraftDto>? ApplyRequested;
    public Func<RecruitmentDraftDto, CancellationToken, Task>? CreateRequested { get; set; }

    public void ApplyText(MissionTextDto e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        // Le hub peut devancer la réponse HTTP qui nous donne l'identifiant de la mission.
        if (_submitting)
        {
            if ((_mission is null || e.MissionId == _mission) && _earlyText.Count < 128 && _earlyChars + e.Text.Length <= 64_000)
            {
                _earlyText.Add(e);
                _earlyChars += e.Text.Length;
            }
            return;
        }
        if (!_waiting || e.MissionId != _mission) return;
        if (_streamingMessage is null)
        {
            _streamingMessage = new RecruitmentMessage(false, "");
            Messages.Add(_streamingMessage);
        }
        _streamingMessage.Text += e.Text;
    }

    private void DrainEarlyText()
    {
        _submitting = false;
        foreach (var chunk in _earlyText) ApplyText(chunk);
        _earlyText.Clear();
        _earlyChars = 0;
    }

    public async Task RestoreAsync(Guid missionId, CancellationToken ct = default)
    {
        IsOpen = true;
        IsBusy = true;
        _mission = missionId;
        _submitting = true;
        _streamingMessage = null;
        try
        {
            var mission = await api.GetMissionAsync(missionId, ct)
                ?? throw new HttpRequestException("L'entretien n'est plus accessible.");
            if (mission.Kind != MissionKind.Recruitment)
                throw new HttpRequestException("Cette mission n'est pas un entretien RH.");
            Messages.Clear();
            Messages.Add(new RecruitmentMessage(true, mission.Goal));
            foreach (var turn in await api.GetTurnsAsync(missionId, ct))
                Messages.Add(new RecruitmentMessage(turn.FromUser, turn.Text));
            _mission = missionId;
            Draft = mission.Status == MissionStatus.Completed ? mission.RecruitmentDraft : null;
            _waiting = mission.Status is MissionStatus.Pending or MissionStatus.Running;
            DrainEarlyText();
            StopCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStop));
            if (_waiting) await WaitAsync(ct);
            else if (mission.Status is MissionStatus.Failed or MissionStatus.Cancelled)
            {
                Error = mission.Result;
                _mission = null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        { Error = "Impossible de reprendre l'entretien : " + ex.Message; }
        finally { _submitting = false; _earlyText.Clear(); _earlyChars = 0; IsBusy = false; }
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    private bool CanSend => !IsBusy && !_waiting && Input.Trim().Length is > 0 and <= 2000;
    private bool CanUseDraft => !IsBusy && Draft is not null;
    public bool CanResume => !IsBusy && _waiting && _mission is not null;
    public bool CanStop => IsBusy && _waiting && _mission is not null;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync(CancellationToken ct)
    {
        try
        {
            await api.StopMissionAsync(_mission!.Value, ct);
            Info = "Arrêt demandé : les échanges précédents restent dans l'historique.";
        }
        catch (HttpRequestException ex) { Error = "L'arrêt n'a pas pu être demandé : " + ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken ct)
    {
        IsBusy = true;
        Error = null;
        Info = null;
        Draft = null; // Une nouvelle demande invalide la proposition précédente.
        _streamingMessage = null;
        _submitting = true;
        var text = Input.Trim();
        try
        {
            if (_mission is null)
                _mission = (await api.CreateMissionAsync(text, ct, MissionKind.Recruitment, projectId)).Id;
            else
                await api.ReplyAsync(_mission.Value, text, ct);
            Messages.Add(new RecruitmentMessage(true, text));
            Input = "";
            _waiting = true;
            DrainEarlyText();
            StopCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStop));
            await WaitAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            Error = "L'entretien n'a pas abouti : " + ex.Message;
        }
        finally { _submitting = false; _earlyText.Clear(); _earlyChars = 0; IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync(CancellationToken ct)
    {
        IsBusy = true;
        Error = null;
        try { await WaitAsync(ct); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        { Error = "L'attente a été interrompue : " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task WaitAsync(CancellationToken ct)
    {
        while (true)
        {
            var mission = await api.GetMissionAsync(_mission!.Value, ct)
                ?? throw new HttpRequestException("Cet entretien n'est plus accessible.");
            if (mission.Status == MissionStatus.Completed)
            {
                var text = mission.Result ?? "Pouvez-vous préciser votre besoin ?";
                if (_streamingMessage is not null)
                    _streamingMessage.Text = text;
                else if (Messages.LastOrDefault() is not { FromUser: false } last || last.Text != text)
                    Messages.Add(new RecruitmentMessage(false, text));
                _streamingMessage = null;
                Draft = mission.RecruitmentDraft;
                _waiting = false;
                return;
            }
            if (mission.Status is MissionStatus.Failed or MissionStatus.Cancelled or MissionStatus.AwaitingApproval)
            {
                Error = mission.Result ?? "L'entretien a été interrompu. Vous pouvez en démarrer un nouveau.";
                _mission = null;
                _waiting = false;
                return;
            }
            await Task.Delay(500, ct);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDraft))]
    private void Apply()
    {
        ApplyRequested?.Invoke(Draft!);
        Info = "La fiche est remplie. Vous pouvez ajuster le prénom, le visage, les consignes et les compétences avant de recruter.";
    }

    [RelayCommand(CanExecute = nameof(CanUseDraft))]
    private async Task CreateAsync(CancellationToken ct)
    {
        if (CreateRequested is null) return;
        IsBusy = true;
        Error = null;
        try { await CreateRequested(Draft!, ct); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { Error = "La création a échoué : " + ex.Message; }
        finally { IsBusy = false; }
    }
}
