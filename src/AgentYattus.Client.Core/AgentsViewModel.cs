using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Une carte de l'écran Agents : l'agent, le visage du collègue qui l'incarne, et ses chiffres.</summary>
public sealed partial class AgentCard(AgentDto agent) : ObservableObject
{
    public AgentDto Agent { get; } = agent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Hello))]
    public partial Face? Face { get; set; }

    /// <summary>Le prénom du collègue ; sans collègue à ce poste, le nom de l'agent.</summary>
    public string Name => Face?.Name ?? Agent.Name;

    /// <summary>Sa bulle sur la fiche : il se présente avec la description de son rôle.</summary>
    public string Hello => $"Bonjour, je suis {Name}. {Agent.Role}";

    /// <summary>Réussite et coût ; null tant qu'on n'a pas le droit de les voir ou qu'il n'a rien terminé.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Missions), nameof(Rate), nameof(RateLabel), nameof(Cost), nameof(HasStats))]
    public partial AgentPerformanceDto? Stats { get; set; }

    public bool HasStats => Stats is { Finished: > 0 };
    public int Missions => Stats?.Finished ?? 0;

    /// <summary>De 0 à 1 : la part des missions terminées qui ont abouti ; -1 sans chiffres.</summary>
    public double Rate => HasStats ? (double)Stats!.Completed / Stats.Finished : -1;
    public string RateLabel => HasStats ? $"{Math.Round(Rate * 100)} %" : "—";
    public string Cost => HasStats ? $"{Stats!.AverageCost:0.####} $" : "—";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Le collègue qui occupe ce poste dans le projet ; null : siège libre.</summary>
    public ColleagueDto? Colleague { get; set; }

    /// <summary>Ses traits, en mots : « Rigoureuse », « Pédagogue ».</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Traits { get; set; } = [];

    /// <summary>Un agent créé dans l'admin porte la mention « PERSO ».</summary>
    public bool IsCustom => !Agent.Builtin;

    /// <summary>Ce qu'il fait en ce moment : « En mission », « Attend votre accord », « Disponible ».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    public partial AgentState State { get; set; }

    public string StateLabel => State switch
    {
        AgentState.Busy => "En mission",
        AgentState.Waiting => "Attend votre accord",
        _ => "Disponible",
    };
}

public enum AgentState { Free, Busy, Waiting }

/// <summary>La dernière tuile de la grille : « Recruter un collègue ».</summary>
public sealed record RecruitTile;

/// <summary>L'écran Agents : l'équipe en visages, la fiche de l'agent choisi, ses outils et ses chiffres.</summary>
/// <param name="project">Le projet dont on montre les collègues ; null : le projet personnel.</param>
/// <param name="projectName">Son nom, sous le titre ; null : le projet personnel.</param>
/// <param name="missions">Les missions de l'accueil : qui est en mission, qui attend votre accord.</param>
public sealed partial class AgentsViewModel(
    ApiClient api, Guid? project = null, string? projectName = null, IEnumerable<MissionItem>? missions = null) : ObservableObject
{
    public ObservableCollection<AgentCard> Agents { get; } = [];

    /// <summary>La grille : les cartes, puis « Recruter un collègue » pour qui peut recruter.</summary>
    public ObservableCollection<object> Tiles { get; } = [];

    /// <summary>« 3 agents · projet Orders.Api ».</summary>
    public string Subtitle => $"{Agents.Count} agents · {(projectName is null ? "projet personnel" : "projet " + projectName)}";

    [ObservableProperty]
    public partial AgentCard? Selected { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>« Confier une mission à … » : la coquille revient à l'accueil avec cet agent choisi.</summary>
    public event Action<AgentDto>? AssignRequested;

    /// <summary>Le projet dont on montre l'équipe : celui de l'accueil, sinon le projet personnel.</summary>
    public Guid? Project => project ?? api.Faces.DefaultProject;

    /// <summary>Changer de visage est réservé au propriétaire et aux éditeurs du projet.</summary>
    [ObservableProperty]
    public partial bool CanCustomize { get; set; }

    /// <summary>« Changer de visage » : la coquille ouvre « Nouveau collègue » sur ce poste.</summary>
    public event Action<Guid, AgentCard?>? CustomizeRequested;

    /// <summary>Le crayon d'une fiche (ce poste, ce collègue), ou la tuile « Recruter » (sans carte).</summary>
    [RelayCommand]
    private void Customize(AgentCard? card)
    {
        if (Project is { } id)
            CustomizeRequested?.Invoke(id, card);
    }

    partial void OnSelectedChanged(AgentCard? oldValue, AgentCard? newValue)
    {
        oldValue?.IsSelected = false;
        newValue?.IsSelected = true;
    }

    [RelayCommand]
    private void Select(AgentCard card) => Selected = card;

    [RelayCommand]
    private void Assign(AgentCard card) => AssignRequested?.Invoke(card.Agent);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Agents.Clear();
            foreach (var agent in await api.GetAgentsAsync(cancellationToken))
                Agents.Add(new AgentCard(agent) { State = StateOf(agent.Key) });
            Selected = Agents.FirstOrDefault();
            OnPropertyChanged(nameof(Subtitle));
            Tiles.Clear();
            foreach (var card in Agents)
                Tiles.Add(card);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
            return;
        }

        foreach (var card in Agents.ToList())
            card.Face = await api.Faces.AgentAsync(project, card.Agent.Key);

        try
        {
            var team = Project is { } id ? await api.GetProjectTeamAsync(id, cancellationToken) : null;
            var labels = team is null ? [] : (await api.GetTraitsAsync(cancellationToken)).ToDictionary(t => t.Key, t => t.Label);
            foreach (var card in Agents)
            {
                card.Colleague = team?.Colleagues.FirstOrDefault(c => c.Active && c.Role == card.Agent.Key);
                card.Traits = [.. card.Colleague?.Traits.Select(k => labels.GetValueOrDefault(k, k)) ?? []];
            }
            CanCustomize = team?.CanRecruit == true;
            if (CanCustomize)
                Tiles.Add(new RecruitTile());
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
        }

        // Les chiffres sont réservés aux administrateurs : sans eux, les cartes affichent « — ».
        try
        {
            var stats = await api.GetPerformanceAsync(cancellationToken) ?? [];
            foreach (var card in Agents)
                card.Stats = stats.FirstOrDefault(s => s.Role == card.Agent.Key);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
        }
    }

    private AgentState StateOf(string role)
    {
        var mine = (missions ?? []).Where(m => m.Roles.Contains(role)).ToList();
        return mine.Any(m => m.IsAwaitingApproval) ? AgentState.Waiting
            : mine.Any(m => m.IsActive) ? AgentState.Busy
            : AgentState.Free;
    }
}
