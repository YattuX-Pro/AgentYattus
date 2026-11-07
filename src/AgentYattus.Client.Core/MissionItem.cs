using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentYattus.Client.Core;

// Une carte de mission : le statut change en direct via le hub.
public sealed partial class MissionItem(MissionDto mission) : ObservableObject
{
    public Guid Id { get; } = mission.Id;
    public string Goal { get; } = mission.Goal;
    public bool IsReview { get; } = mission.Kind == MissionKind.Review;
    public DateTimeOffset CreatedAt { get; } = mission.CreatedAt;

    /// <summary>La réponse de l'agent, une fois la mission terminée.</summary>
    public string? Result { get; } = mission.Result;

    /// <summary>Le projet de la mission : son équipe joue la scène du bureau (ADR 0012).</summary>
    public Guid? ProjectId { get; } = mission.ProjectId;

    /// <summary>L'agent qui fait la mission (ADR 0013) ; null : l'Analyste.</summary>
    public string? Agent { get; } = mission.Agent;

    /// <summary>Qui y travaille : l'agent choisi, ou l'Analyste puis le Rédacteur pour une revue.</summary>
    public IReadOnlyList<string> Roles { get; } =
        mission.Kind == MissionKind.Review ? [Scene.Analyst, Scene.Writer] : [mission.Agent ?? Scene.Analyst];

    /// <summary>Leurs visages, dans l'ordre de <see cref="Roles"/> ; un siège libre n'a pas de photo, seulement un nom.</summary>
    public ObservableCollection<Face> Faces { get; } = [];

    /// <summary>« Inès », ou « Inès et Malik » pour une revue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    public partial string? Who { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(IsActive), nameof(IsCompleted), nameof(IsFailed), nameof(IsAwaitingApproval), nameof(AgentLabel), nameof(Line))]
    public partial MissionStatus Status { get; set; } = mission.Status;

    public bool IsActive => Status is MissionStatus.Pending or MissionStatus.Running;
    public bool IsCompleted => Status == MissionStatus.Completed;
    public bool IsFailed => Status == MissionStatus.Failed;
    public bool IsAwaitingApproval => Status == MissionStatus.AwaitingApproval;

    public string StatusLabel => Status switch
    {
        MissionStatus.Pending => "En attente",
        MissionStatus.Running => "En cours",
        MissionStatus.Completed => "Terminée",
        MissionStatus.AwaitingApproval => "À valider",
        MissionStatus.Cancelled => "Arrêtée",
        _ => "Échec",
    };

    /// <summary>Ce que fait l'agent, vu de l'écran Mission.</summary>
    public string AgentLabel => Status switch
    {
        MissionStatus.Pending => "en attente",
        MissionStatus.Running => "au travail",
        MissionStatus.AwaitingApproval => "en pause",
        MissionStatus.Completed => "terminé",
        MissionStatus.Cancelled => "arrêté",
        _ => "échec",
    };

    /// <summary>La ligne sous la carte, dite avec les prénoms : « Malik frappe à votre porte ».</summary>
    public string Line
    {
        get
        {
            var first = Faces.FirstOrDefault()?.Name ?? "L'agent";
            var last = Faces.LastOrDefault()?.Name ?? "L'agent";
            return Status switch
            {
                MissionStatus.Pending => $"{first} va s'y mettre",
                MissionStatus.Running => $"{first} y travaille",
                MissionStatus.AwaitingApproval => $"{last} frappe à votre porte",
                MissionStatus.Completed => Who is null ? "Terminée" : $"Terminée · {Who}",
                MissionStatus.Cancelled => "Arrêtée",
                _ => Who is null ? "Échec" : $"Échec · {Who}",
            };
        }
    }

    /// <summary>Va chercher les visages de ceux qui travaillent sur la mission.</summary>
    public async Task LoadFacesAsync(FaceBook faces)
    {
        if (Faces.Count > 0)
            return;
        foreach (var role in Roles)
            Faces.Add(await faces.AgentAsync(ProjectId, role) ?? new Face(Label(role), null, null));
        Who = string.Join(" et ", Faces.Select(f => f.Name));
    }

    /// <summary>« analyste-securite » → « Analyste securite » : le nom d'un siège sans collègue.</summary>
    internal static string Label(string role) => role.Length == 0 ? role : char.ToUpperInvariant(role[0]) + role[1..].Replace('-', ' ');
}
