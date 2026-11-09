using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

public sealed partial class WorkspaceMessage(string name, string text, bool fromUser, byte[]? avatar = null, long id = 0, Guid? missionId = null, string? recipient = null, DateTimeOffset? at = null) : ObservableObject
{
    public string Time => at?.ToLocalTime().ToString("HH:mm") ?? "";
    public Guid? MissionId { get; } = missionId;
    public string? Recipient { get; } = recipient;
    public string Address => string.IsNullOrEmpty(Recipient) ? Name : $"{Name} → {Recipient}";
    public string Name { get; } = name;
    public bool FromUser { get; } = fromUser;
    public byte[]? Avatar { get; } = avatar;
    public long Id { get; } = id;
    [ObservableProperty] public partial string Text { get; set; } = text;
}

public sealed partial class ConversationContact(MessageAuthorDto author, byte[]? avatar, bool leader = false) : ObservableObject
{
    public MessageAuthorDto Author { get; } = author;
    public string Key => Author.ColleagueId?.ToString() ?? Author.Role;
    public string Name => Author.Name;
    public string Role => Author.Role;
    public byte[]? Avatar { get; } = avatar;
    public string Label => leader ? "Chef d’équipe" : Role == "_rh" ? "Recrutement" : "Membre du groupe";
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsUnread { get; set; }
    [ObservableProperty] public partial MissionDto? Mission { get; set; }
    public string Draft { get; set; } = "";
}

/// <summary>Un espace stable par projet : destinataire explicite, activité, livrables et RH dans le même fil.</summary>
public sealed partial class WorkspaceViewModel(ApiClient api, Guid projectId, string projectName, string userName) : ObservableObject
{
    private IReadOnlyList<MissionDto> _missions = [];
    private readonly Dictionary<Guid, RecruitViewModel> _recruitments = [];
    private readonly Dictionary<Guid, long> _sequences = [];
    private readonly Dictionary<Guid, WorkspaceMessage> _streamed = [];
    private readonly List<MissionTextDto> _early = [];
    private bool _submitting;
    private int _selectionVersion;
    private bool _loading;
    private readonly Dictionary<Guid, (string Name, DateTimeOffset? LastText)> _working = [];
    [ObservableProperty] public partial bool HasWorkingAgents { get; set; }
    [ObservableProperty] public partial string WorkingLabel { get; set; } = "";
    public void UpdateWorkingState()
    {
        HasWorkingAgents = _working.Count > 0;
        WorkingLabel = string.Join(" · ", _working.Values.Take(3).Select(a =>
            a.LastText is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(8)
                ? $"{a.Name} écrit" : $"{a.Name} travaille"));
    }
    private int _groupLimit = 100;
    private bool _groupDraftLoaded;
    private readonly Dictionary<Guid, long> _loadedGroupTurns = [];
    private readonly Dictionary<Guid, Guid> _executions = [];
    private readonly Dictionary<Guid, Guid> _streamMissions = [];
    private Guid? _profileMission;
    public ObservableCollection<MissionItem> PendingTasks { get; } = [];
    private bool _restoringDraft;
    private CancellationTokenSource? _draftDebounce;
    public Guid ProjectId { get; } = projectId;
    public ObservableCollection<ProjectDto> Projects { get; } = [];
    public event Action<ProjectDto>? ProjectChanged;
    [ObservableProperty] public partial ProjectDto? SelectedProject { get; set; }
    partial void OnSelectedProjectChanged(ProjectDto? value)
    {
        if (value is not null && value.Id != ProjectId) ProjectChanged?.Invoke(value);
    }
    public string ProjectName { get; } = projectName;
    public ObservableCollection<ConversationContact> Contacts { get; } = [];
    public ObservableCollection<WorkspaceMessage> Messages { get; } = [];
    public ObservableCollection<MissionItem> Activity { get; } = [];
    public ObservableCollection<MissionItem> Deliverables { get; } = [];
    public ObservableCollection<TimelineEntry> Timeline { get; } = [];
    public ObservableCollection<ApprovalItem> Approvals { get; } = [];
    public ObservableCollection<PortraitChoice> EmptyPortraits { get; } = [];
    public event Action<MissionItem>? OfficeRequested;
    public event Action? ContentLoaded;
    public event Action? ReadingChanged;
    public bool IsPresented { get; set; }
    public void Presented() { IsPresented = true; ReadingChanged?.Invoke(); }
    public event Action? RecruitmentCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(Recipient), nameof(IsRh), nameof(CanSend), nameof(CanStop))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    public partial ConversationContact? Selected { get; set; }
    [ObservableProperty] public partial ConversationContact? Addressee { get; set; }
    partial void OnAddresseeChanged(ConversationContact? value)
    {
        if (value is not null && value != Selected) _ = OpenContactAsync(value);
    }

    [ObservableProperty] public partial string StatusText { get; set; } = "Groupe du projet · le chef d’équipe coordonne les demandes";
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsConnected { get; set; } = true;
    [ObservableProperty] public partial bool IsReadingLatest { get; set; } = true;
    [ObservableProperty] public partial bool HasNewMessages { get; set; }
    [ObservableProperty] public partial string Tab { get; set; } = "Conversation";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfile))]
    public partial RecruitmentDraftDto? Profile { get; set; }
    [ObservableProperty] public partial RecruitViewModel? RecruitmentSetup { get; set; }
    [ObservableProperty] public partial bool RecruitmentAdded { get; set; }
    [ObservableProperty] public partial bool HasApproval { get; set; }
    [ObservableProperty] public partial bool AllApprovalShown { get; set; }
    public Guid? ActiveMissionId => Selected?.Mission?.Id;
    public long LoadedTurnId { get; private set; }
    public string Heading => "Groupe du projet";
    public string Recipient => Selected is null ? "Message au groupe" : $"Dans le groupe · réponse attendue de {Selected.Name}";
    public bool IsRh => Selected?.Role == "_rh";
    public bool CanAcceptResult => Selected?.Mission is { Status: MissionStatus.Completed, CanReply: true, NeedsReply: false, ResultAccepted: false }
        && !IsRh && LoadedTurnId > 0 && !string.IsNullOrWhiteSpace(Selected.Mission.Result);
    [RelayCommand]
    private async Task AcceptResultAsync()
    {
        if (!CanAcceptResult || ActiveMissionId is not { } id) return;
        try { await api.AcceptResultAsync(id, LoadedTurnId); await LoadCurrentAsync(); }
        catch (HttpRequestException) { Error = "Le résultat a changé ou n’est plus accessible. Actualisez avant de le valider."; }
    }
    public bool HasProfile => Profile is not null && !RecruitmentAdded;
    public bool CanSend => Selected is not null && !IsBusy && Input.Trim().Length is > 0 and <= 4000
        && SelectedProject?.MyAccess is ProjectRole.Owner or ProjectRole.Editor;
    public bool CanStop => Selected?.Mission?.Status is MissionStatus.Pending or MissionStatus.Running;
    public bool IsConversation => Tab == "Conversation";
    public bool IsActivity => Tab == "Activité";
    public bool IsDeliverables => Tab == "Livrables";
    public string? RhHandoff => Selected?.Mission?.RecruitmentMissionId?.ToString();

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsConversation)); OnPropertyChanged(nameof(IsActivity)); OnPropertyChanged(nameof(IsDeliverables));
    }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanSend)); SendCommand.NotifyCanExecuteChanged(); }
    partial void OnInputChanged(string value)
    {
        if (_restoringDraft || Selected is null) return;
        Selected.Draft = value;
        _draftDebounce?.Cancel();
        _draftDebounce = new CancellationTokenSource();
        _ = SaveDraftLaterAsync("group", value, _draftDebounce.Token);
    }
    private async Task SaveDraftLaterAsync(string contact, string value, CancellationToken ct)
    {
        try { await Task.Delay(500, ct); await api.SaveContactDraftAsync(ProjectId, contact, value, ct); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { IsConnected = false; }
    }
    private void RestoreInput(string value)
    {
        _restoringDraft = true; Input = value; _restoringDraft = false;
    }
    public async Task SaveDraftAsync()
    {
        _draftDebounce?.Cancel();
        if (Selected is { } contact)
        {
            contact.Draft = Input;
            try { await api.SaveContactDraftAsync(ProjectId, "group", Input); }
            catch (HttpRequestException) { IsConnected = false; }
        }
    }

    public async Task LoadAsync(Guid? openMission = null, CancellationToken ct = default)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var latest = await api.ListMissionsAsync(100, ct, ProjectId);
            _missions = latest.Concat(_missions.Where(m => latest.All(n => n.Id != m.Id)))
                .OrderByDescending(m => m.Id).ToArray();
            var team = await api.GetProjectTeamAsync(ProjectId, ct);
            if (Projects.Count == 0)
            {
                foreach (var p in await api.GetProjectsAsync(ct)) Projects.Add(p);
                SelectedProject = Projects.FirstOrDefault(p => p.Id == ProjectId);
            }
            {
                foreach (var colleague in (team?.Colleagues ?? []).OrderByDescending(c => c.Leader))
                {
                    if (Contacts.Any(c => c.Author.ColleagueId == colleague.Id)) continue;
                    var author = new MessageAuthorDto(colleague.Role, colleague.Name, colleague.Id, colleague.PortraitId);
                    var face = await api.Faces.HistoricalAsync(author);
                    Contacts.Add(new ConversationContact(author, face?.Work, colleague.Leader));
                }
                if (Contacts.All(c => c.Role != "_rh")) Contacts.Add(new ConversationContact(new("_rh", "Agent RH"), null));
            }
            foreach (var mission in _missions.Where(m => m.Kind is MissionKind.Question or MissionKind.Recruitment))
            {
                var author = mission.Interlocutor ?? new MessageAuthorDto(mission.Kind == MissionKind.Recruitment ? "_rh" : mission.Agent ?? "analyste",
                    mission.Kind == MissionKind.Recruitment ? "Agent RH" : mission.Agent ?? "Analyste");
                var contact = Contacts.FirstOrDefault(c => author.ColleagueId is { } id ? c.Author.ColleagueId == id : c.Role == author.Role);
                if (contact is null)
                {
                    var face = await api.Faces.HistoricalAsync(author);
                    contact = new ConversationContact(author, face?.Work);
                    Contacts.Add(contact);
                }
                if (contact != Selected && (contact.Mission is null || mission.Id.CompareTo(contact.Mission.Id) >= 0)) contact.Mission = mission;
            }
            Activity.Clear(); Deliverables.Clear(); PendingTasks.Clear();
            foreach (var mission in _missions)
            {
                Activity.Add(new MissionItem(mission));
                if (mission.Status is MissionStatus.Pending or MissionStatus.Running or MissionStatus.AwaitingApproval || mission.NeedsReply)
                    PendingTasks.Add(new MissionItem(mission));
                if (mission.Status == MissionStatus.Completed && (mission.Kind is MissionKind.Review or MissionKind.Develop || mission.ResultAccepted) && !string.IsNullOrWhiteSpace(mission.Result))
                    Deliverables.Add(new MissionItem(mission));
            }
            IsConnected = true;
            if (openMission is { } target)
            {
                var requested = _missions.FirstOrDefault(m => m.Id == target) ?? await api.GetMissionAsync(target, ct);
                if (requested?.ProjectId == ProjectId) await OpenMissionAsync(new MissionItem(requested));
            }
            else if (Selected is null && Contacts.FirstOrDefault() is { } first) await OpenContactAsync(first);
            else if (Selected?.Mission is { } selected && _missions.FirstOrDefault(m => m.Id == selected.Id) is { } fresh)
            { Selected.Mission = fresh; await LoadCurrentAsync(ct); }
            await LoadGroupAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        { IsConnected = false; Error = "Le projet n’a pas pu être actualisé. Vos brouillons sont conservés."; }
        finally { _loading = false; }
    }

    [RelayCommand]
    private async Task OpenContactAsync(ConversationContact contact)
    {
        await SaveDraftAsync();
        _selectionVersion++;
        Selected = contact; Addressee = contact;
        foreach (var item in Contacts) item.IsSelected = item == contact;
        Tab = "Conversation"; RecruitmentSetup = null; RecruitmentAdded = false;
        Error = null;
        if (!_groupDraftLoaded)
        {
            try { RestoreInput(await api.GetContactDraftAsync(ProjectId, "group")); _groupDraftLoaded = true; }
            catch (HttpRequestException) { }
        }
        if (contact.Mission is not null) await LoadCurrentAsync();
        else { StatusText = $"{contact.Name} répondra dans le groupe"; RefreshCommands(); }
        await LoadGroupAsync();
    }

    private async Task LoadCurrentAsync(CancellationToken ct = default)
    {
        if (Selected?.Mission is not { } current) return;
        var version = _selectionVersion;
        var dto = await api.GetMissionAsync(current.Id, ct);
        if (dto is null || version != _selectionVersion || ActiveMissionId != current.Id) return;
        var turns = await api.GetTurnsAsync(current.Id, ct);
        if (version != _selectionVersion || ActiveMissionId != current.Id) return;
        Selected!.Mission = dto;
        if (dto.ExecutionId is { } activeExecution) _executions[dto.Id] = activeExecution;

        HasApproval = dto.PendingApproval is not null;
        if (dto.PendingApproval?.Id != _approvalId) AllApprovalShown = false;
        _approvalId = dto.PendingApproval?.Id;
        Approvals.Clear();
        foreach (var call in dto.PendingApproval?.ToolCalls ?? []) Approvals.Add(ApprovalItem.From(call));
        StatusText = dto.Status switch
        {
            MissionStatus.Pending => "En attente d’un créneau",
            MissionStatus.Running => "Prépare sa réponse · vous pouvez continuer ailleurs",
            MissionStatus.AwaitingApproval => "À vous · votre accord est nécessaire",
            MissionStatus.Completed => dto.NeedsReply ? "À vous · une précision est attendue" : dto.ResultAccepted ? "Résultat validé" : "Réponse reçue",
            MissionStatus.Failed => dto.Result ?? "La tâche n’a pas abouti",
            _ => "Tâche arrêtée",
        };
        LoadedTurnId = turns.Select(t => t.Id).DefaultIfEmpty().Max();
        await LoadGroupAsync(ct);
        if (Tab == "Activité") await LoadTimelineAsync(ct);
        OnPropertyChanged(nameof(RhHandoff)); RefreshCommands(); ContentLoaded?.Invoke();
    }
    private async Task LoadGroupAsync(CancellationToken ct = default)
    {
        foreach (var id in _working.Keys.Where(id => !_missions.Any(m => m.Id == id && m.Status == MissionStatus.Running)).ToArray()) _working.Remove(id);
        foreach (var running in _missions.Where(m => m.Status == MissionStatus.Running))
            if (!_working.ContainsKey(running.Id)) _working[running.Id] = (running.Interlocutor?.Name ?? (running.Kind == MissionKind.Recruitment ? "Agent RH" : running.Agent ?? "Un agent"), null);
        UpdateWorkingState();
        var feed = await api.GetGroupMessagesAsync(ProjectId, _groupLimit, ct);
        var rows = new List<WorkspaceMessage>();
        foreach (var entry in feed)
        {
            var face = await api.Faces.HistoricalAsync(entry.Author);
            rows.Add(new WorkspaceMessage(entry.Author?.Name ?? (entry.FromUser ? userName : "Agent · identité historique non disponible"),
                entry.Text, entry.FromUser, face?.Talk ?? face?.Work, entry.TurnId, entry.MissionId, entry.Recipient, entry.At));
        }
        _loadedGroupTurns.Clear();
        foreach (var entry in feed)
            _loadedGroupTurns[entry.MissionId] = Math.Max(_loadedGroupTurns.GetValueOrDefault(entry.MissionId), entry.TurnId);
        foreach (var pair in _streamed.ToArray())
        {
            if (!_streamMissions.TryGetValue(pair.Key, out var mission)) continue;
            if (_missions.FirstOrDefault(m => m.Id == mission)?.Status is MissionStatus.Pending or MissionStatus.Running)
                rows.Add(pair.Value);
            else { _streamed.Remove(pair.Key); _streamMissions.Remove(pair.Key); }
        }
        for (var i = 0; i < rows.Count; i++)
        {
            if (i >= Messages.Count) Messages.Add(rows[i]);
            else if (Messages[i].MissionId != rows[i].MissionId || Messages[i].Id != rows[i].Id || Messages[i].Name != rows[i].Name || Messages[i].Text != rows[i].Text)
                Messages[i] = rows[i];
        }
        while (Messages.Count > rows.Count) Messages.RemoveAt(Messages.Count - 1);
        var rh = _missions.FirstOrDefault(m => m.Kind == MissionKind.Recruitment && m.RecruitmentDraft is not null && !m.RecruitmentAdded);
        _profileMission = rh?.Id; Profile = rh?.RecruitmentDraft; RecruitmentAdded = rh?.RecruitmentAdded ?? false;
        ContentLoaded?.Invoke();
    }

    private Guid? _approvalId;
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanSend)); OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanAcceptResult));
        SendCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken ct)
    {
        var contact = Selected!;
        var version = _selectionVersion;
        var input = Input.Trim();
        IsBusy = true; Error = null; _submitting = true; _early.Clear();
        try
        {
            MissionDto dto;
            if (contact.Mission is not { CanReply: true, Status: MissionStatus.Completed })
                dto = await api.CreateMissionAsync(input, ct, contact.Role == "_rh" ? MissionKind.Recruitment : MissionKind.Question,
                    ProjectId, contact.Role == "_rh" ? null : contact.Role);
            else
            {
                await api.ReplyAsync(contact.Mission.Id, input, ct);
                dto = contact.Mission with { Status = MissionStatus.Pending, ExecutionId = null, CanReply = false };
            }
            contact.Mission = dto; contact.Draft = "";
            _missions = _missions.Where(m => m.Id != dto.Id).Prepend(dto).ToArray();
            if (version == _selectionVersion)
            {
                RestoreInput(""); Messages.Add(new WorkspaceMessage(userName, input, true, missionId: dto.Id, recipient: contact.Name));
                StatusText = "Demande reçue · en attente de réponse";
            }
            try { await api.SaveContactDraftAsync(ProjectId, "group", "", ct); } catch (HttpRequestException) { }
            if (version == _selectionVersion) await LoadCurrentAsync(ct);
            _submitting = false;
            foreach (var piece in _early) ApplyText(piece);
            _early.Clear();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { Error = "L’envoi n’a pas été confirmé. Votre texte est conservé ; actualisez avant de réessayer."; }
        finally { _submitting = false; _early.Clear(); IsBusy = false; RefreshCommands(); }
    }
    public void ApplyText(MissionTextDto piece)
    {
        if (_submitting) { if (_early.Count < 256 && _early.Sum(e => e.Text.Length) < 64000) _early.Add(piece); return; }
        var mission = _missions.FirstOrDefault(m => m.Id == piece.MissionId);
        if (mission is null || mission.Status is not (MissionStatus.Pending or MissionStatus.Running)) return;
        if (piece.ExecutionId is { } received && _executions.TryGetValue(piece.MissionId, out var execution) && execution != received) return;
        var id = piece.MessageId ?? piece.MissionId;
        if (piece.Sequence > 0 && _sequences.GetValueOrDefault(id) >= piece.Sequence) return;
        _sequences[id] = piece.Sequence;
        if (!_streamed.TryGetValue(id, out var message))
        {
            var avatar = Contacts.FirstOrDefault(c => piece.Author?.ColleagueId is { } person ? c.Author.ColleagueId == person : c.Role == piece.Author?.Role)?.Avatar;
            message = new WorkspaceMessage(piece.Author?.Name ?? "Agent", "", false, avatar, missionId: piece.MissionId);
            _streamed[id] = message; _streamMissions[id] = piece.MissionId; Messages.Add(message);
        }
        _working[piece.MissionId] = (piece.Author?.Name ?? "Un agent", DateTimeOffset.UtcNow); UpdateWorkingState();
        message.Text += piece.Text; StatusText = $"{piece.Author?.Name ?? Selected?.Name} écrit…";
        if (!IsReadingLatest) HasNewMessages = true;
    }
    public void ApplyActivity(MissionActivityDto activity)
    {
        if (!_missions.Any(m => m.Id == activity.MissionId)) return;
        _executions[activity.MissionId] = activity.ExecutionId;
        _working[activity.MissionId] = (activity.Author.Name, null); UpdateWorkingState();
        StatusText = $"{activity.Author.Name} · {activity.Text}";
    }
    public async Task ApplyAsync(MissionEventDto e)
    {
        await LoadAsync();
    }
    public bool HasRead(ConversationNoticeDto notice) => IsPresented && IsConversation && IsReadingLatest && notice.ProjectId == ProjectId
        && _loadedGroupTurns.TryGetValue(notice.MissionId, out var last)
        && (notice.TurnId is { } id ? last >= id : _missions.FirstOrDefault(m => m.Id == notice.MissionId)?.Status == notice.Status);

    [RelayCommand]
    private async Task OlderConversationsAsync()
    {
        var before = Activity.LastOrDefault()?.Id;
        if (before is null) return;
        try
        {
            _groupLimit = Math.Min(500, _groupLimit + 100);
            var older = await api.ListMissionsAsync(100, projectId: ProjectId, before: before);
            _missions = _missions.Concat(older).DistinctBy(m => m.Id).OrderByDescending(m => m.Id).ToArray();
            foreach (var dto in older)
            {
                if (Activity.Any(m => m.Id == dto.Id)) continue;
                Activity.Add(new MissionItem(dto));
                if (dto.Status == MissionStatus.Completed && (dto.Kind is MissionKind.Review or MissionKind.Develop || dto.ResultAccepted))
                    Deliverables.Add(new MissionItem(dto));
            }
            await LoadGroupAsync();
        }
        catch (HttpRequestException) { Error = "Les échanges anciens n’ont pas pu être chargés."; }
    }

    [RelayCommand] private void ShowConversation() => Tab = "Conversation";
    [RelayCommand] private async Task ShowActivityAsync() { Tab = "Activité"; await LoadTimelineAsync(); }
    [RelayCommand] private void ShowDeliverables() => Tab = "Livrables";
    private async Task LoadTimelineAsync(CancellationToken ct = default)
    {
        Timeline.Clear();
        if (ActiveMissionId is not { } id) return;
        foreach (var e in await api.GetEventsAsync(id, ct)) Timeline.Add(TimelineEntry.From(e));
    }
    [RelayCommand]
    public async Task OpenMissionAsync(MissionItem item)
    {
        var dto = await api.GetMissionAsync(item.Id);
        if (dto is null || dto.ProjectId != ProjectId) return;
        var contact = Contacts.FirstOrDefault(c => dto.Interlocutor?.ColleagueId is { } person
            ? c.Author.ColleagueId == person : c.Role == (dto.Kind == MissionKind.Recruitment ? "_rh" : dto.Agent ?? "analyste"));
        if (contact is null)
        {
            var author = dto.Interlocutor ?? new MessageAuthorDto(dto.Agent ?? "analyste", "Conversation");
            var face = await api.Faces.HistoricalAsync(author);
            contact = new ConversationContact(author, face?.Work); Contacts.Add(contact);
        }
        contact.Mission = dto; await OpenContactAsync(contact);
    }
    [RelayCommand]
    private async Task OpenRhAsync()
    {
        if (Selected?.Mission?.RecruitmentMissionId is { } id && await api.GetMissionAsync(id) is { } dto)
            await OpenMissionAsync(new MissionItem(dto));
        else if (Contacts.FirstOrDefault(c => c.Role == "_rh") is { } rh) await OpenContactAsync(rh);
    }
    [RelayCommand] private void OpenOffice() { if (Selected?.Mission is { } m) OfficeRequested?.Invoke(new MissionItem(m)); }
    [RelayCommand]
    private async Task NewConversationAsync()
    {
        await SaveDraftAsync();
        if (Selected is null) return;
        Selected.Mission = null;
        RecruitmentSetup = null; HasApproval = false; StatusText = "Nouvelle demande dans le groupe"; RefreshCommands();
    }
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync() { if (ActiveMissionId is { } id) { await api.StopMissionAsync(id); await LoadCurrentAsync(); } }
    [RelayCommand]
    private async Task ApproveAsync() => await DecideAsync(true);
    [RelayCommand]
    private async Task RefuseAsync() => await DecideAsync(false);
    private async Task DecideAsync(bool approved)
    {
        if (ActiveMissionId is not { } id || _approvalId is not { } approval) return;
        if (approved && !AllApprovalShown && Approvals.Any(a => a.HasMoreLines))
        { Error = "Dépliez et relisez les détails avant de donner votre accord."; return; }
        if (!await api.DecideAsync(id, approval, approved)) Error = "Cette demande n’est plus d’actualité.";
        await LoadCurrentAsync();
    }
    [RelayCommand]
    private async Task PrepareRecruitmentAsync()
    {
        if (Profile is null || RecruitmentAdded || _profileMission is not { } id) return;
        var requestId = _missions.FirstOrDefault(m => m.Id == id)?.RecruitmentRequestId ?? id;
        if (!_recruitments.TryGetValue(requestId, out var setup))
        {
            setup = new RecruitViewModel(api, ProjectId, projectName: ProjectName, recruitmentSource: requestId, recruitmentMission: id);
            await setup.LoadAsync(); setup.PrepareProfile(Profile);
            if (setup.Portraits.FirstOrDefault() is { } portrait) setup.PickCommand.Execute(portrait);
            setup.Done += () => { RecruitmentSetup = null; RecruitmentAdded = true; StatusText = "Le collègue a rejoint l’équipe"; api.Faces.Forget(ProjectId); RecruitmentCompleted?.Invoke(); };
            _recruitments[requestId] = setup;
        }
        RecruitmentSetup = setup;
    }
    [RelayCommand] private void CloseRecruitment() => RecruitmentSetup = null;
}
