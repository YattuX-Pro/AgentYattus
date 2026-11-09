using System.Collections.ObjectModel;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentYattus.Client.Core;

// Une carte de mission : le statut change en direct via le hub.
public sealed partial class MissionItem(MissionDto mission) : ObservableObject
{
    public Guid Id { get; } = mission.Id;
    public Guid? RecruitmentMissionId { get; } = mission.RecruitmentMissionId;
    public string Goal { get; } = mission.Goal;
    public bool IsReview { get; } = mission.Kind == MissionKind.Review;

    /// <summary>Une mission Développer (ADR 0016) : le Développeur, puis le Relecteur.</summary>
    public bool IsDevelop { get; } = mission.Kind == MissionKind.Develop;
    public bool IsRecruitment { get; } = mission.Kind == MissionKind.Recruitment;
    public DateTimeOffset CreatedAt { get; } = mission.CreatedAt;

    /// <summary>Coût estimé, en dollars (ADR 0010).</summary>
    public decimal Cost { get; } = mission.Cost;

    /// <summary>« Aujourd’hui, 10:42 », « Hier, 17:05 », sinon « 3 novembre, 09:12 ».</summary>
    public string When
    {
        get
        {
            var local = CreatedAt.ToLocalTime();
            var days = (DateTime.Today - local.Date).Days;
            var day = days switch { 0 => "Aujourd’hui", 1 => "Hier", _ => local.ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) };
            return $"{day}, {local:HH:mm}";
        }
    }

    /// <summary>La réponse de l'agent, une fois la mission terminée.</summary>
    public string? Result { get; } = mission.Result;

    /// <summary>Le projet de la mission : son équipe joue la scène du bureau (ADR 0012).</summary>
    public Guid? ProjectId { get; } = mission.ProjectId;

    /// <summary>L'agent qui fait la mission (ADR 0013) ; null : l'Analyste.</summary>
    public string? Agent { get; } = mission.Kind == MissionKind.Recruitment ? "rh" : mission.Agent;

    /// <summary>La branche git de la mission (mode branche, leçon 127) ; null sinon.</summary>
    public string? Branch { get; } = mission.Branch;

    /// <summary>Qui y travaille : l'agent choisi, ou l'Analyste puis le Rédacteur pour une revue.</summary>
    public IReadOnlyList<string> Roles { get; } =
        mission.Kind == MissionKind.Review ? [Scene.Analyst, Scene.Writer]
        : mission.Kind == MissionKind.Develop ? [Scene.Developer, Scene.Reviewer]
        : [mission.Kind == MissionKind.Recruitment ? "rh" : mission.Agent ?? Scene.Analyst];

    /// <summary>Leurs visages, dans l'ordre de <see cref="Roles"/> ; un siège libre n'a pas de photo, seulement un nom.</summary>
    public ObservableCollection<Face> Faces { get; } = [];

    /// <summary>« Inès », ou « Inès et Malik » pour une revue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    public partial string? Who { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(IsActive), nameof(IsCompleted), nameof(IsFailed), nameof(IsAwaitingApproval), nameof(AgentLabel), nameof(Line), nameof(ShortLine), nameof(Ago))]
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

    /// <summary>La ligne du panneau des missions, sans prénom : le visage dit déjà qui.</summary>
    public string ShortLine => Status switch
    {
        MissionStatus.Pending => "En attente",
        MissionStatus.Running => "En cours…",
        MissionStatus.AwaitingApproval => "Attend votre accord",
        MissionStatus.Completed => "Terminée",
        MissionStatus.Cancelled => "Arrêtée",
        _ => "Échec",
    };

    /// <summary>« en cours », « 2 min », « 3 h », « hier », sinon « 3 nov. ».</summary>
    public string Ago => Elapsed(IsActive, CreatedAt, DateTimeOffset.Now);

    public static string Elapsed(bool active, DateTimeOffset at, DateTimeOffset now)
    {
        if (active)
            return "en cours";
        var elapsed = now - at;
        var days = (now.LocalDateTime.Date - at.LocalDateTime.Date).Days;
        return elapsed.TotalMinutes < 1 ? "à l’instant"
            : elapsed.TotalMinutes < 60 ? $"{(int)elapsed.TotalMinutes} min"
            : days == 0 ? $"{(int)elapsed.TotalHours} h"
            : days == 1 ? "hier"
            : at.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
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
