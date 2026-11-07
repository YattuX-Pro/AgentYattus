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
}

/// <summary>L'écran Agents : l'équipe en visages, la fiche de l'agent choisi, ses outils et ses chiffres.</summary>
/// <param name="project">Le projet dont on montre les collègues ; null : le projet personnel.</param>
public sealed partial class AgentsViewModel(ApiClient api, Guid? project = null) : ObservableObject
{
    public ObservableCollection<AgentCard> Agents { get; } = [];

    [ObservableProperty]
    public partial AgentCard? Selected { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>« Confier une mission à … » : la coquille revient à l'accueil avec cet agent choisi.</summary>
    public event Action<AgentDto>? AssignRequested;

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
                Agents.Add(new AgentCard(agent));
            Selected = Agents.FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
            return;
        }

        foreach (var card in Agents.ToList())
            card.Face = await api.Faces.AgentAsync(project, card.Agent.Key);

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
}
