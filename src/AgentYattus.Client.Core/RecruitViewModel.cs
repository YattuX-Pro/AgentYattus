using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AgentYattus.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Un visage de la galerie, avec son image et son état de sélection.</summary>
public sealed partial class PortraitChoice(PortraitDto portrait) : ObservableObject
{
    public PortraitDto Portrait { get; } = portrait;

    [ObservableProperty]
    public partial byte[]? Image { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Un trait proposé (réglé dans l'admin), coché ou non.</summary>
public sealed partial class TraitChoice(TraitDto trait) : ObservableObject
{
    public TraitDto Trait { get; } = trait;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Un poste : un rôle d'agent existant (ADR 0012) ; ses outils et son modèle viennent de l'admin.</summary>
public sealed partial class RoleChoice(AgentDto agent) : ObservableObject
{
    public string Key { get; } = agent.Key;
    public string Name { get; } = agent.Name;

    /// <summary>Ce que le rôle permet, pour l'afficher : « list_files, read_file ».</summary>
    public string Tools { get; } = string.Join(", ", agent.Tools.Select(t => t.Name));

    public string Model { get; } = agent.Model;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// L'écran « Nouveau collègue » : choisir un visage dans la galerie, un prénom, un poste et des traits.
/// Les outils et le modèle sont ceux du rôle, réglés dans l'admin : on les montre, on ne les choisit pas ici.
/// </summary>
public sealed partial class RecruitViewModel(ApiClient api, Guid projectId) : ObservableObject
{
    public Guid ProjectId { get; } = projectId;

    public ObservableCollection<PortraitChoice> Portraits { get; } = [];
    public ObservableCollection<TraitChoice> Traits { get; } = [];
    public ObservableCollection<RoleChoice> Roles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intro), nameof(DisplayName))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial PortraitChoice? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intro))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial RoleChoice? Role { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Le recrutement est fait : la coquille revient à l'open space, qui recharge son équipe.</summary>
    public event Action? Done;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Sans nom" : Name.Trim();

    /// <summary>Sa première bulle, telle qu'elle apparaîtra : le prénom, le poste, et les traits choisis.</summary>
    public string Intro
    {
        get
        {
            var traits = Traits.Where(t => t.IsSelected).Select(t => t.Trait.Label.ToLowerInvariant()).ToList();
            var role = Role?.Name.ToLowerInvariant() ?? "nouveau collègue";
            return $"Bonjour, je suis {DisplayName}, {role}." + (traits.Count > 0 ? $" On me dit {string.Join(", ", traits)}." : "");
        }
    }

    /// <summary>Même règle que le serveur : le prénom entre dans la consigne, des lettres seulement.</summary>
    public bool IsNameValid => NamePattern().IsMatch(Name.Trim());

    private bool CanRecruit => !IsBusy && Selected is not null && Role is not null && IsNameValid;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var portraits = await api.GetPortraitsAsync(cancellationToken);
            var traits = await api.GetTraitsAsync(cancellationToken);
            var agents = await api.GetAgentsAsync(cancellationToken);
            Portraits.Clear();
            foreach (var p in portraits)
                Portraits.Add(new PortraitChoice(p));
            Traits.Clear();
            foreach (var t in traits)
            {
                var choice = new TraitChoice(t);
                choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Intro));
                Traits.Add(choice);
            }

            Roles.Clear();
            foreach (var a in agents)
                Roles.Add(new RoleChoice(a));
            if (Roles.FirstOrDefault() is { } first)
                PickRole(first);
            if (Portraits.FirstOrDefault() is { } face)
                Pick(face);
            Error = null;

            // Les visages arrivent ensuite, un par un : la galerie s'affiche tout de suite.
            foreach (var p in Portraits)
                p.Image = await api.GetPortraitImageAsync(p.Portrait.Id, "travail", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Error = ex.Message;
        }
    }

    /// <summary>Choisir un visage propose son prénom, tant qu'on n'en a pas tapé un autre.</summary>
    [RelayCommand]
    private void Pick(PortraitChoice choice)
    {
        var suggested = Selected?.Portrait.SuggestedName;
        foreach (var p in Portraits)
            p.IsSelected = p == choice;
        Selected = choice;
        if (string.IsNullOrWhiteSpace(Name) || Name == suggested)
            Name = choice.Portrait.SuggestedName;
    }

    [RelayCommand]
    private void PickRole(RoleChoice choice)
    {
        foreach (var r in Roles)
            r.IsSelected = r == choice;
        Role = choice;
    }

    [RelayCommand(CanExecute = nameof(CanRecruit))]
    private async Task RecruitAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Error = null;
        try
        {
            await api.RecruitAsync(ProjectId, new RecruitRequest(
                Role!.Key, Selected!.Portrait.Id, Name.Trim(), [.. Traits.Where(t => t.IsSelected).Select(t => t.Trait.Key)]), cancellationToken);
            Done?.Invoke();
        }
        catch (HttpRequestException ex)
        {
            Error = ex.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "Seuls le propriétaire et les éditeurs du projet recrutent."
                : $"Recrutement refusé : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Done?.Invoke();

    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}' \-]{0,23}$")]
    private static partial Regex NamePattern();
}
