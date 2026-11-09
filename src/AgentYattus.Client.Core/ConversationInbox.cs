using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

public sealed record ConversationNotice(ConversationNoticeDto Data, byte[]? Avatar)
{
    public Guid Id => Data.Id;
    public string Name => Data.Author?.Name ?? "Agent Yattus";
    public string Text => Data.Text;
    public string Project => Data.ProjectName ?? "Projet personnel";
    public string Action => Data.Status == MissionStatus.AwaitingApproval ? "Examiner" : "Ouvrir la conversation";
}

/// <summary>La lecture est persistée ; fermer le nuage ne lit pas le message.</summary>
public sealed partial class ConversationInbox(ApiClient api) : ObservableObject
{
    private readonly HashSet<Guid> _shown = [];
    private readonly SemaphoreSlim _refresh = new(1);
    private CancellationTokenSource? _dismiss;
    private int _generation;
    public ObservableCollection<ConversationNotice> Items { get; } = [];
    public Func<ConversationNoticeDto, bool>? IsReadOnScreen { get; set; }
    public Func<Guid, Task>? OpenRequested { get; set; }
    [ObservableProperty] public partial ConversationNotice? Toast { get; set; }
    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    public int Count => Items.Count;
    public bool HasUnread => Count > 0;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refresh.WaitAsync(0, ct)) return;
        var generation = _generation;
        try
        {
            var incoming = await api.GetNoticesAsync(ct);
            if (generation != _generation) return;
            var current = incoming.Select(n => n.Id).ToHashSet();
            if (Toast is { } shown && !current.Contains(shown.Id)) Toast = null;
            foreach (var old in Items.Where(n => !current.Contains(n.Id)).ToList()) Items.Remove(old);
            ConversationNotice? nextToast = null;
            foreach (var notice in incoming)
            {
                if (IsReadOnScreen?.Invoke(notice) == true)
                {
                    await api.ReadNoticeAsync(notice.Id, ct);
                    var old = Items.FirstOrDefault(n => n.Id == notice.Id);
                    if (old is not null) Items.Remove(old);
                    if (Toast?.Id == notice.Id) Toast = null;
                    _shown.Add(notice.Id);
                    continue;
                }
                var item = Items.FirstOrDefault(n => n.Id == notice.Id);
                if (item is null)
                {
                    var face = await api.Faces.HistoricalAsync(notice.Author);
                    if (generation != _generation) return;
                    item = new(notice, face?.Talk ?? face?.Work); Items.Insert(0, item);
                }
                if (_shown.Add(notice.Id)) nextToast = item;
            }
            if (Toast is null && nextToast is not null) { Toast = nextToast; ResumeToast(); }
            Error = null; NotifyCount();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        { Error = "Les réponses non lues seront actualisées à la reconnexion."; }
        finally { _refresh.Release(); }
    }
    private void NotifyCount() { OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(HasUnread)); }
    public void PauseToast() => _dismiss?.Cancel();
    public void ResumeToast()
    {
        _dismiss?.Cancel(); _dismiss = new CancellationTokenSource();
        _ = DismissLaterAsync(_dismiss.Token);
    }
    private async Task DismissLaterAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(8), ct); Toast = null; }
        catch (OperationCanceledException) { }
    }
    [RelayCommand] private void DismissToast() { PauseToast(); Toast = null; }
    [RelayCommand] private void Toggle() => IsOpen = !IsOpen;
    [RelayCommand] private async Task OpenToastAsync() { if (Toast is { } item) await OpenAsync(item); }
    [RelayCommand]
    private async Task OpenAsync(ConversationNotice item)
    {
        PauseToast(); Toast = null; IsOpen = false;
        try { if (OpenRequested is not null) await OpenRequested(item.Data.MissionId); }
        catch (HttpRequestException) { Error = "Cette conversation n’est plus accessible."; }
    }
    public void Clear()
    {
        _generation++;
        PauseToast(); Items.Clear(); _shown.Clear(); Toast = null; IsOpen = false; Error = null; NotifyCount();
    }
}
