using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentYattus.Client.Core;

// Une carte de mission : le statut change en direct via le hub.
public sealed partial class MissionItem(MissionDto mission) : ObservableObject
{
    public Guid Id { get; } = mission.Id;
    public string Goal { get; } = mission.Goal;
    public bool IsReview { get; } = mission.Kind == MissionKind.Review;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(IsActive), nameof(IsCompleted), nameof(IsFailed), nameof(IsAwaitingApproval), nameof(AgentLabel))]
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
        _ => "Échec",
    };

    /// <summary>Ce que fait l'agent, vu de l'écran Mission.</summary>
    public string AgentLabel => Status switch
    {
        MissionStatus.Pending => "en attente",
        MissionStatus.Running => "au travail",
        MissionStatus.AwaitingApproval => "en pause",
        MissionStatus.Completed => "terminé",
        _ => "échec",
    };
}
