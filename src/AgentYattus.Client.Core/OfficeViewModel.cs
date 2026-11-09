using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Un bureau de l'open space : un collègue, ses deux poses, sa bulle et ce qu'il fait.</summary>
public sealed partial class DeskViewModel(string role, string roleLabel) : ObservableObject
{
    public string Role { get; } = role;

    [ObservableProperty]
    public partial string RoleLabel { get; set; } = roleLabel;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial byte[]? WorkImage { get; set; }

    [ObservableProperty]
    public partial byte[]? TalkImage { get; set; }

    /// <summary>Vrai pendant que sa bulle s'écrit et reste affichée : la pose « parle » remplace « travaille ».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Layer))]
    public partial bool IsSpeaking { get; set; }

    /// <summary>Celui qui parle passe au premier plan : sa bulle ne se cache pas sous le bureau d'un voisin.</summary>
    public int Layer => IsSpeaking ? 10 : 1;

    [ObservableProperty]
    public partial string Bubble { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "disponible";

    /// <summary>Siège libre : aucun collègue n'occupe ce rôle dans le projet.</summary>
    public bool IsEmpty => WorkImage is null;
}

/// <summary>Une ligne du fil de l'équipe.</summary>
public sealed record ChatLine(string Name, string Text, string Time, byte[]? Avatar, string? File, bool IsLeader);

/// <summary>
/// L'open space d'une mission (ADR 0012) : les collègues à leur bureau, les bulles qui s'écrivent, ce qui circule,
/// et la demande d'accord. La décision elle-même reste celle de <see cref="MissionViewModel"/> : relire tout avant d'approuver.
/// </summary>
/// <remarks>
/// Aucun minuteur ici : la vue appelle <see cref="Tick"/> à chaque image. Les tests avancent le temps à la main.
/// </remarks>
public sealed partial class OfficeViewModel : ObservableObject
{
    /// <summary>Le plan de l'open space, en unités de la vue (1016 × 804) : départ et arrivée de chaque objet.</summary>
    private static readonly Dictionary<SceneObject, (double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3)> Routes = new()
    {
        [SceneObject.Folder] = (104, 250, 170, 210, 260, 270, 330, 330),
        [SceneObject.Notes] = (340, 330, 440, 250, 540, 250, 640, 330),
        [SceneObject.Report] = (640, 360, 700, 430, 790, 500, 846, 600),
    };

    private const double TravelMs = 1900;

    private readonly ApiClient _api;
    private readonly string _you;

    // L'agent installé au bureau de l'analyste la dernière fois qu'on a habillé les bureaux.
    private string? _dressedWorker;
    private readonly Dictionary<Guid, byte[]?> _images = [];
    private ProjectTeamDto? _team;
    private IReadOnlyList<SceneLine> _script = [];
    private int _current;
    private double _typedMs;
    private double _heldMs;
    private bool _firstLoad = true;

    public OfficeViewModel(ApiClient api, MissionItem mission, string you)
    {
        _api = api;
        _you = you;
        Mission = new MissionViewModel(api, mission);
        Leader = new DeskViewModel(Scene.Leader, "CHEF D'ÉQUIPE");
        Analyst = new DeskViewModel(Scene.Analyst, "ANALYSTE");
        Writer = new DeskViewModel(Scene.Writer, "RÉDACTEUR");
        Manager = new DeskViewModel(Scene.You, "RESPONSABLE") { Name = "Vous" };
        Conversation.Add(new ChatLine(_you, mission.Goal, mission.CreatedAt.ToLocalTime().ToString("HH:mm"), null, null, false));
        mission.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MissionItem.Status))
                NotifyConversationState();
        };
        // Après une décision, la mission se recharge : la porte se referme, et la réplique du rédacteur se joue.
        Mission.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MissionViewModel.IsDecisionPending))
                return;
            OnPropertyChanged(nameof(IsKnocking));
            OnKnockChanged();
            if (!IsKnocking)
                IsReviewing = false;
            // Pendant le premier chargement, le journal n'est pas encore lu : LoadAsync reconstruira la scène lui-même.
            if (!_firstLoad)
                Rebuild();
        };
    }

    /// <summary>La mission et sa décision (approuver après avoir tout relu, refuser).</summary>
    public MissionViewModel Mission { get; }

    /// <summary>Le projet de la mission, dans la barre : « Orders.Api » ; null pour le projet personnel.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Le bureau du chef d'équipe : le collègue recruté avec ce titre (leçon 129), sinon un siège libre.</summary>
    public DeskViewModel Leader { get; }

    /// <summary>Le poste du chef d'équipe dans ce projet ; null s'il n'y en a pas.</summary>
    private string? LeaderRole => _team?.Colleagues.FirstOrDefault(c => c.Leader)?.Role;

    /// <summary>
    /// L'agent au bureau de l'analyste : celui à qui le chef a confié la demande, sinon l'agent de la mission (ADR 0013),
    /// sinon l'analyste. Le chef, lui, travaille toujours depuis son propre bureau.
    /// </summary>
    private string Worker =>
        Mission.Mission.IsDevelop ? Scene.Developer
        : Mission.Events.LastOrDefault(e => e.Type.StartsWith("delegue:", StringComparison.Ordinal)) is { } delegated ? delegated.Type["delegue:".Length..]
        : Mission.Mission.Agent is { } agent && agent != LeaderRole ? agent
        : Scene.Analyst;
    public DeskViewModel Analyst { get; }
    public DeskViewModel Writer { get; }
    public DeskViewModel Manager { get; }

    public ObservableCollection<ChatLine> Feed { get; } = [];

    /// <summary>Le vrai dialogue, indépendant des répliques animées de la scène.</summary>
    public ObservableCollection<ChatLine> Conversation { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendReplyCommand))]
    public partial string ReplyText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReply))]
    [NotifyCanExecuteChangedFor(nameof(SendReplyCommand))]
    public partial bool IsSending { get; set; }

    public bool CanReply => Mission.Mission.IsCompleted && !IsSending;
    public bool IsResponding => Mission.Mission.IsActive;
    public string ConversationTitle => string.IsNullOrWhiteSpace(Author.Name) ? "Conversation" : $"Conversation avec {Author.Name}";
    private bool CanSendReply => CanReply && !string.IsNullOrWhiteSpace(ReplyText);

    private void NotifyConversationState()
    {
        OnPropertyChanged(nameof(CanReply));
        OnPropertyChanged(nameof(IsResponding));
        OnPropertyChanged(nameof(Author));
        OnPropertyChanged(nameof(ConversationTitle));
        SendReplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSendReply))]
    private async Task SendReplyAsync(CancellationToken cancellationToken)
    {
        IsSending = true;
        var text = ReplyText.Trim();
        try
        {
            await _api.ReplyAsync(Mission.Mission.Id, text, cancellationToken);
            Conversation.Add(new ChatLine(_you, text, DateTimeOffset.Now.ToString("HH:mm"), Manager.WorkImage, null, false));
            ReplyText = "";
            LiveText = null;
            Mission.Mission.Status = MissionStatus.Pending;
            Error = null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }

    private async Task LoadConversationAsync(CancellationToken ct)
    {
        IReadOnlyList<MissionTurnDto> turns = [];
        try
        {
            turns = await _api.GetTurnsAsync(Mission.Mission.Id, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // Une ancienne API peut ne pas encore exposer le fil : le résultat reste disponible.
        }
        Conversation.Clear();
        Conversation.Add(new ChatLine(_you, Mission.Mission.Goal, Mission.Mission.CreatedAt.ToLocalTime().ToString("HH:mm"), Manager.WorkImage, null, false));
        foreach (var turn in turns)
        {
            var face = await _api.Faces.HistoricalAsync(turn.Author);
            Conversation.Add(new ChatLine(turn.FromUser ? _you : face?.Name ?? "Agent · identité historique non disponible", turn.Text,
                turn.At.ToLocalTime().ToString("HH:mm"), turn.FromUser ? Manager.WorkImage : face?.Talk ?? face?.Work,
                null, !turn.FromUser && turn.Author?.Role == LeaderRole));
        }
        if (turns.Count == 0 && !string.IsNullOrWhiteSpace(Mission.Result))
            Conversation.Add(new ChatLine(Author.Name, Mission.Result, "", Author.TalkImage ?? Author.WorkImage, null, Author == Leader));
        if (!Mission.Mission.IsActive)
            LiveText = null;
        NotifyConversationState();
    }

    [ObservableProperty]
    public partial bool CanRecruit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarrying), nameof(CarryLabel))]
    public partial SceneObject Carrying { get; set; }

    public bool IsCarrying => Carrying != SceneObject.None;

    public string CarryLabel => Carrying switch
    {
        SceneObject.Folder => _script.ElementAtOrDefault(_current)?.File ?? "dossier",
        SceneObject.Notes => "constats",
        SceneObject.Report => _script.ElementAtOrDefault(_current)?.File ?? "REPORT.md",
        _ => "",
    };

    [ObservableProperty]
    public partial double TokenX { get; set; }

    [ObservableProperty]
    public partial double TokenY { get; set; }

    /// <summary>Le nom de qui écrit en ce moment, pour « … écrit » sous le fil.</summary>
    [ObservableProperty]
    public partial string? Typing { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    private void OnKnockChanged()
    {
        OnPropertyChanged(nameof(Asker));
        OnPropertyChanged(nameof(IsLearning));
        OnPropertyChanged(nameof(KnockTitle));
        OnPropertyChanged(nameof(KnockText));
        OnPropertyChanged(nameof(ReviewTitle));
    }

    /// <summary>Le bureau vitré passe en laiton : quelqu'un frappe à la porte.</summary>
    public bool IsKnocking => Mission.IsDecisionPending;

    /// <summary>Qui frappe : le rédacteur dans une revue, sinon l'agent de la mission, au bureau de l'analyste.</summary>
    public DeskViewModel Asker => Mission.Mission.IsReview ? Writer : Analyst;

    /// <summary>La demande est une compétence à retenir (leçon 106), pas un fichier à écrire.</summary>
    public bool IsLearning => Mission.PendingApproval.FirstOrDefault()?.Tool == "propose_skill";

    public string KnockTitle => $"{Asker.Name} frappe à votre porte";

    private string? PendingTool => Mission.PendingApproval.FirstOrDefault()?.Tool;

    public string KnockText => PendingTool switch
    {
        "propose_skill" => "Il veut retenir une compétence pour la prochaine fois. Relisez-la avant de décider.",
        "run_command" => "Il veut lancer une commande dans le bac à sable. Relisez-la avant de décider.",
        "git_commit" => "Il veut enregistrer un commit sur la branche de la mission. Relisez le message avant de décider.",
        _ => "Il veut écrire dans le workspace. Relisez tout avant de décider.",
    };

    public string ReviewTitle => PendingTool switch
    {
        "propose_skill" => $"{Asker.Name} veut retenir ceci",
        "run_command" => $"{Asker.Name} veut lancer ceci",
        "git_commit" => $"{Asker.Name} veut enregistrer ce commit",
        _ => $"{Asker.Name} veut écrire ceci",
    };

    /// <summary>La relecture est ouverte : exactement ce qui sera écrit, avant d'approuver ou de refuser.</summary>
    [ObservableProperty]
    public partial bool IsReviewing { get; set; }

    [RelayCommand]
    private void OpenReview() => IsReviewing = IsKnocking;

    [RelayCommand]
    private void CloseReview() => IsReviewing = false;

    /// <summary>Recruter depuis l'open space : la coquille ouvre l'écran Nouveau collègue.</summary>
    public event Action<Guid>? RecruitmentOpened;

    [RelayCommand]
    private void OpenRecruitment()
    {
        if (Mission.RecruitmentMissionId is { } id)
            RecruitmentOpened?.Invoke(id);
    }

    public event Action<Guid>? RecruitRequested;

    [RelayCommand(CanExecute = nameof(CanRecruit))]
    private void Recruit()
    {
        if (Mission.Mission.ProjectId is { } project)
            RecruitRequested?.Invoke(project);
    }

    partial void OnCanRecruitChanged(bool value) => RecruitCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dress = false;
            if (_team is null && Mission.Mission.ProjectId is { } project)
            {
                _team = await _api.GetProjectTeamAsync(project, cancellationToken);
                dress = _team is not null;
            }

            await Mission.LoadAsync(cancellationToken);
            // Après le journal : le bureau de l'analyste revient au collègue à qui le chef a confié la demande.
            if (_team is not null && (dress || _dressedWorker != Worker))
                await DressDesksAsync(_team, cancellationToken);
            OnPropertyChanged(nameof(IsKnocking));
            OnKnockChanged();
            Rebuild();
            await LoadConversationAsync(cancellationToken);
            Error = Mission.Error;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }

    /// <summary>Après un recrutement : les bureaux reprennent les visages et prénoms de l'équipe à jour.</summary>
    public Task ReloadTeamAsync()
    {
        _team = null;
        return LoadAsync();
    }

    /// <summary>Un événement du hub : on recharge si c'est notre mission, et les nouvelles répliques se jouent à la suite.</summary>
    public Task ApplyAsync(MissionEventDto e)
    {
        if (e.MissionId != Mission.Mission.Id)
            return Task.CompletedTask;
        // Une nouvelle exécution (relance, réponse) : la réponse en direct repart de zéro.
        if (e.Status is MissionStatus.Pending)
            LiveText = null;
        return LoadAsync();
    }

    /// <summary>La réponse en train de s'écrire (leçon 120), dans le fil de l'équipe ; elle reste une fois finie.</summary>
    [ObservableProperty]
    public partial string? LiveText { get; set; }

    /// <summary>Qui écrit la réponse : le rédacteur dans une revue, sinon le collègue à qui le chef l'a confiée, sinon l'agent de la mission.</summary>
    public DeskViewModel Author => Mission.Mission.IsReview ? DeskOf(Scene.Writer) ?? Writer
        : Mission.Events.Any(e => e.Type.StartsWith("delegue:", StringComparison.Ordinal)) ? DeskOf(Worker) ?? Analyst
        : DeskOf(Mission.Mission.Agent ?? Scene.Analyst) ?? Analyst;

    public void ApplyText(MissionTextDto e)
    {
        if (e.MissionId == Mission.Mission.Id)
            LiveText += e.Text;
    }

    private async Task DressDesksAsync(ProjectTeamDto team, CancellationToken ct)
    {
        CanRecruit = team.CanRecruit;
        var leader = team.Colleagues.FirstOrDefault(c => c.Leader);
        Leader.Name = leader?.Name ?? "Siège libre";
        await DressAsync(Leader, leader?.PortraitId, ct);
        await DressAsync(Manager, team.ManagerPortraitId, ct);
        var worker = _dressedWorker = Worker;
        Analyst.RoleLabel = worker == Scene.Analyst ? "ANALYSTE" : worker.Replace('-', ' ').ToUpperInvariant();
        foreach (var desk in new[] { Analyst, Writer })
        {
            // Le chef a son bureau : son siège de poste reste libre, chacun n'apparaît qu'une fois.
            var role = desk == Analyst ? worker : desk.Role;
            var colleague = role == leader?.Role ? null : team.Colleagues.FirstOrDefault(c => c.Role == role);
            desk.Name = colleague?.Name ?? "Siège libre";
            await DressAsync(desk, colleague?.PortraitId, ct);
        }
    }

    private async Task DressAsync(DeskViewModel desk, Guid? portrait, CancellationToken ct)
    {
        desk.WorkImage = await ImageAsync(portrait, "travail", ct);
        desk.TalkImage = await ImageAsync(portrait, "parle", ct) ?? desk.WorkImage;
    }

    private async Task<byte[]?> ImageAsync(Guid? portrait, string pose, CancellationToken ct)
    {
        if (portrait is not { } id)
            return null;
        var cacheKey = pose == "parle" ? XorPose(id) : id;
        if (!_images.TryGetValue(cacheKey, out var bytes))
            _images[cacheKey] = bytes = await _api.GetPortraitImageAsync(id, pose, ct);
        return bytes;
    }

    // Deux poses du même portrait : la seconde clé se déduit de la première en inversant un octet.
    private static Guid XorPose(Guid id)
    {
        var bytes = id.ToByteArray();
        bytes[15] ^= 0xFF;
        return new Guid(bytes);
    }

    /// <summary>
    /// Recalcule les répliques depuis le journal complet. Les répliques déjà jouées le restent ; les nouvelles se jouent
    /// à la suite. Au premier chargement, tout ce qui s'est déjà passé entre directement dans le fil, sauf la dernière
    /// réplique, qui se joue : on ouvre une mission terminée sans attendre qu'elle se rejoue.
    /// </summary>
    private void Rebuild()
    {
        if (_team is null)
            return;
        var file = Mission.PendingApproval.FirstOrDefault()?.Target;
        _script = Scene.From(Mission.Events, _team, Mission.Mission.Goal, _you, file, Worker);
        if (_firstLoad)
        {
            _firstLoad = false;
            for (; _current < _script.Count - 1; _current++)
                Publish(_script[_current]);
        }
    }

    /// <summary>Fait avancer la scène : la bulle s'écrit lettre par lettre, l'objet voyage, puis la réplique entre dans le fil.</summary>
    public void Tick(TimeSpan elapsed)
    {
        if (_team is null || _current >= _script.Count)
        {
            Typing = null;
            return;
        }

        var line = _script[_current];
        var desk = DeskOf(line.Speaker);
        var typingMs = Math.Max(1, _team.TypingMs);
        _typedMs += elapsed.TotalMilliseconds;
        var chars = (int)Math.Min(line.Text.Length, _typedMs / typingMs);

        if (desk is not null)
        {
            foreach (var other in Desks.Where(d => d != desk))
                other.IsSpeaking = false;
            desk.IsSpeaking = true;
            desk.Bubble = line.Text[..chars];
        }

        Typing = chars < line.Text.Length ? desk?.Name : null;
        Carrying = line.Carries;
        if (line.Carries != SceneObject.None)
            Move(line.Carries, Math.Min(1, _typedMs / TravelMs));

        if (chars < line.Text.Length)
            return;
        _heldMs += elapsed.TotalMilliseconds;
        if (_heldMs < _team.PauseMs)
            return;

        Publish(line);
        if (desk is not null)
            desk.IsSpeaking = false;
        Carrying = SceneObject.None;
        _current++;
        _typedMs = 0;
        _heldMs = 0;
    }

    /// <summary>La réplique entre dans le fil, et chaque bureau prend le statut qu'elle annonce.</summary>
    private void Publish(SceneLine line)
    {
        var desk = DeskOf(line.Speaker);
        Feed.Add(new ChatLine(desk?.Name ?? line.Speaker, line.Text, line.At.ToLocalTime().ToString("HH:mm"), desk?.WorkImage,
            line.Carries == SceneObject.None ? null : line.File, desk == Leader));
        foreach (var (role, status) in line.Statuses)
            if (DeskOf(role) is { } d)
                d.Status = status;
    }

    private IEnumerable<DeskViewModel> Desks => [Leader, Analyst, Writer, Manager];

    // Le chef d'équipe parle depuis son bureau, quel que soit son poste ; l'agent de la mission, depuis celui de l'analyste.
    private DeskViewModel? DeskOf(string speaker) =>
        speaker == LeaderRole ? Leader : speaker == Worker ? Analyst : Desks.FirstOrDefault(d => d.Role == speaker);

    // Une courbe de Bézier cubique, avec une accélération douce au départ et à l'arrivée.
    private void Move(SceneObject what, double t)
    {
        var r = Routes[what];
        var e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        var u = 1 - e;
        TokenX = u * u * u * r.X0 + 3 * u * u * e * r.X1 + 3 * u * e * e * r.X2 + e * e * e * r.X3;
        TokenY = u * u * u * r.Y0 + 3 * u * u * e * r.Y1 + 3 * u * e * e * r.Y2 + e * e * e * r.Y3;
    }
}
