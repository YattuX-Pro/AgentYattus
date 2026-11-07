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

    /// <summary>La pose « parle », pour l'aperçu de sa première bulle ; chargée quand on le choisit.</summary>
    [ObservableProperty]
    public partial byte[]? Talk { get; set; }

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
    public AgentDto Agent { get; } = agent;
    public string Key { get; } = agent.Key;
    public string Name { get; } = agent.Name;

    /// <summary>Ce que le rôle permet, pour l'afficher : « list_files, read_file ».</summary>
    public string Tools { get; } = string.Join(", ", agent.Tools.Select(t => t.Name));

    public string Model { get; } = agent.Model;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Un outil, tel que le montre « Ce qu'il ou elle a le droit de faire » : allumé si le poste l'a.</summary>
public sealed partial class ToolRow(ToolInfoDto tool) : ObservableObject
{
    public ToolInfoDto Tool { get; } = tool;

    public string Label { get; } = tool.Name switch
    {
        "list_files" => "Parcourir les dossiers",
        "read_file" => "Lire un fichier",
        "write_file" => "Écrire un fichier",
        _ => tool.Description,
    };

    public string Note { get; } = tool.Name + (tool.RequiresApproval ? " · votre accord à chaque fois" : " · lecture");
    public bool Warn { get; } = tool.RequiresApproval;

    [ObservableProperty]
    public partial bool IsOn { get; set; }
}

/// <summary>
/// L'écran « Nouveau collègue » : choisir un visage dans la galerie, un prénom, un poste et des traits.
/// Les outils et le modèle sont ceux du rôle, réglés dans l'admin : on les montre, on ne les choisit pas ici.
/// Depuis l'écran Agents, il sert aussi à changer de visage : le poste est fixé, le collègue actuel pré-rempli.
/// </summary>
/// <param name="role">Le poste à pourvoir ; null : au choix.</param>
/// <param name="current">Le collègue qui l'occupe : son visage, son prénom et ses traits sont repris.</param>
/// <param name="projectName">Sous le titre : « ÉQUIPE ORDERS.API ».</param>
public sealed partial class RecruitViewModel(
    ApiClient api, Guid projectId, string? role = null, ColleagueDto? current = null, string? projectName = null) : ObservableObject
{
    public Guid ProjectId { get; } = projectId;

    /// <summary>« ÉQUIPE ORDERS.API », ou « ÉQUIPE PERSONNELLE ».</summary>
    public string Team => projectName is null ? "ÉQUIPE PERSONNELLE" : $"ÉQUIPE {projectName.ToUpperInvariant()}";

    /// <summary>Le lien de retour, en haut à gauche : l'écran d'où l'on vient.</summary>
    public string BackTo { get; init; } = "Open space";

    /// <summary>Tous les outils des postes ; ceux du poste choisi sont allumés (réglés dans l'admin).</summary>
    public ObservableCollection<ToolRow> Tools { get; } = [];

    /// <summary>« Nouveau collègue », ou « Nouveau visage pour Inès » quand on remplace quelqu'un.</summary>
    public string Title => current is null ? "Nouveau collègue" : $"Nouveau visage pour {current.Name}";

    /// <summary>Le bouton : recruter, ou garder le poste avec ce nouveau visage.</summary>
    public string Action => current is null ? $"Recruter {DisplayName}" : "Enregistrer ce visage";

    public ObservableCollection<PortraitChoice> Portraits { get; } = [];
    public ObservableCollection<TraitChoice> Traits { get; } = [];
    public ObservableCollection<RoleChoice> Roles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intro), nameof(DisplayName), nameof(Action))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Matricule))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial PortraitChoice? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intro), nameof(Poste), nameof(Access), nameof(ModelShort))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial RoleChoice? Role { get; set; }

    /// <summary>Le poste sur le badge : « ANALYSTE ».</summary>
    public string Poste => (Role?.Name ?? "Nouveau collègue").ToUpperInvariant();

    /// <summary>Sur le badge : lecture seule, ou écriture avec votre accord.</summary>
    public string Access => Role?.Agent.Tools.Any(t => t.RequiresApproval) == true ? "Lecture, écriture validée" : "Lecture seule";

    /// <summary>Le modèle sans son fournisseur : « claude-sonnet-5-5 ».</summary>
    public string ModelShort => Role?.Model.Split('/').Last() ?? "—";

    /// <summary>Le numéro du visage dans la galerie : « AY-0005 ».</summary>
    public string Matricule => Selected is null ? "AY-0000" : $"AY-{Portraits.IndexOf(Selected) + 1:0000}";

    /// <summary>Ses deux premiers traits, sur le badge.</summary>
    public string StyleShort
    {
        get
        {
            var traits = Traits.Where(t => t.IsSelected).Select(t => t.Trait.Label).Take(2).ToList();
            return traits.Count > 0 ? string.Join(", ", traits) : "—";
        }
    }

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
                var choice = new TraitChoice(t) { IsSelected = current?.Traits.Contains(t.Key) == true };
                choice.PropertyChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(Intro));
                    OnPropertyChanged(nameof(StyleShort));
                };
                Traits.Add(choice);
            }

            Roles.Clear();
            foreach (var a in agents)
                Roles.Add(new RoleChoice(a));
            Tools.Clear();
            foreach (var tool in agents.SelectMany(a => a.Tools).DistinctBy(t => t.Name))
                Tools.Add(new ToolRow(tool));
            if ((Roles.FirstOrDefault(r => r.Key == role) ?? Roles.FirstOrDefault()) is { } first)
                PickRole(first);
            if ((Portraits.FirstOrDefault(p => p.Portrait.Id == current?.PortraitId) ?? Portraits.FirstOrDefault()) is { } face)
                Pick(face);
            if (current is not null)
                Name = current.Name;
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
        if (choice.Talk is null && choice.Portrait.HasTalk)
            _ = LoadTalkAsync(choice);
    }

    private async Task LoadTalkAsync(PortraitChoice choice)
    {
        try
        {
            choice.Talk = await api.GetPortraitImageAsync(choice.Portrait.Id, "parle");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    [RelayCommand]
    private void PickRole(RoleChoice choice)
    {
        foreach (var r in Roles)
            r.IsSelected = r == choice;
        Role = choice;
        foreach (var t in Tools)
            t.IsOn = choice.Agent.Tools.Any(x => x.Name == t.Tool.Name);
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
