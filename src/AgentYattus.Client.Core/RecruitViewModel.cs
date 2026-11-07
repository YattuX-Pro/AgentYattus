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
public sealed partial class RoleChoice(AgentDto agent, bool custom = false) : ObservableObject
{
    /// <summary>Le poste « Personnalisé » : un agent à soi, écrit de zéro.</summary>
    public static RoleChoice Personalized() => new(new AgentDto("Personnalisé", "", "", 0, [], ""), custom: true);

    public bool IsCustomSlot { get; } = custom;
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

/// <summary>Une compétence qu'on peut lui donner (leçon 104), cochée ou non.</summary>
public sealed partial class SkillChoice(SkillDto skill) : ObservableObject
{
    public SkillDto Skill { get; } = skill;

    /// <summary>Au survol : quand s'en servir, et combien de fois un agent l'a chargée.</summary>
    public string Tip => Skill.Uses == 0 ? Skill.Description : $"{Skill.Description}\nChargée {Skill.Uses} fois.";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Un modèle proposé ; <see cref="Selection"/> null : celui du poste.</summary>
public sealed record ModelChoice(LlmSelectionDto? Selection, string Label);

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

    /// <summary>Tous les outils ; ceux du poste choisi sont allumés, et chacun se règle d'un clic.</summary>
    public ObservableCollection<ToolRow> Tools { get; } = [];

    /// <summary>Les modèles qu'on peut lui donner : celui du poste, puis ceux des connexions de l'instance.</summary>
    public ObservableCollection<ModelChoice> Models { get; } = [];

    /// <summary>Sa consigne : celle du poste (si on peut la lire), ou la vôtre.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial string Instructions { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelShort))]
    public partial ModelChoice? Model { get; set; }

    /// <summary>Le texte gris d'une consigne vide.</summary>
    public string InstructionsHint => Role is { IsCustomSlot: true }
        ? "Décrivez ce qu’il ou elle doit faire, et comment (20 caractères au moins)."
        : "Laissez vide pour garder la consigne du poste, ou écrivez la vôtre.";

    // Ce que le poste donne au départ : on ne crée un agent à soi que si l'on s'en écarte.
    private string _baseInstructions = "";
    private HashSet<string> _baseTools = [];

    /// <summary>« Nouveau collègue », ou « Nouveau visage pour Inès » quand on remplace quelqu'un.</summary>
    public string Title => current is null ? "Nouveau collègue" : $"Nouveau visage pour {current.Name}";

    /// <summary>Le bouton : recruter, ou garder le poste avec ce nouveau visage.</summary>
    public string Action => current is null ? $"Recruter {DisplayName}" : "Enregistrer ce visage";

    public ObservableCollection<PortraitChoice> Portraits { get; } = [];
    public ObservableCollection<TraitChoice> Traits { get; } = [];
    public ObservableCollection<RoleChoice> Roles { get; } = [];

    /// <summary>Ses compétences : chargées seulement quand sa tâche en a besoin. On les coche, ou on en écrit une.</summary>
    public ObservableCollection<SkillChoice> Skills { get; } = [];

    /// <summary>Vrai : le petit formulaire « Écrire une compétence » est ouvert.</summary>
    [ObservableProperty]
    public partial bool IsWritingSkill { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSkillCommand))]
    public partial string SkillName { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSkillCommand))]
    public partial string SkillDescription { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSkillCommand))]
    public partial string SkillBody { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intro), nameof(DisplayName), nameof(Action))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Matricule))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial PortraitChoice? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intro), nameof(Poste), nameof(Access), nameof(ModelShort), nameof(InstructionsHint))]
    [NotifyCanExecuteChangedFor(nameof(RecruitCommand))]
    public partial RoleChoice? Role { get; set; }

    /// <summary>Le poste sur le badge : « ANALYSTE ».</summary>
    public string Poste => (Role?.Name ?? "Nouveau collègue").ToUpperInvariant();

    /// <summary>Sur le badge : lecture seule, ou écriture avec votre accord.</summary>
    public string Access => Tools.Any(t => t.IsOn && t.Warn) ? "Lecture, écriture validée" : "Lecture seule";

    /// <summary>Le modèle sans son fournisseur : « claude-sonnet-5-5 ».</summary>
    public string ModelShort => (Model?.Selection?.Model ?? Role?.Model)?.Split('/').Last() is { Length: > 0 } m ? m : "—";

    /// <summary>Vrai : on s'écarte du poste (consigne, outils, modèle) ou on part de zéro ; le recrutement crée un agent à soi.</summary>
    public bool IsPersonalized => Role is { IsCustomSlot: true }
        || Instructions.Trim() != _baseInstructions.Trim()
        || !Tools.Where(t => t.IsOn).Select(t => t.Tool.Name).ToHashSet().SetEquals(_baseTools)
        || Model?.Selection is not null && Model.Selection != Role?.Agent.ModelOverride;

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

    private bool CanRecruit => !IsBusy && Selected is not null && Role is not null && IsNameValid && Tools.Any(t => t.IsOn)
        && (Role.IsCustomSlot ? Instructions.Trim().Length >= 20 : Instructions.Trim().Length is 0 or >= 20);

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
            Roles.Add(RoleChoice.Personalized());
            Tools.Clear();
            foreach (var tool in agents.SelectMany(a => a.Tools).DistinctBy(t => t.Name))
            {
                var row = new ToolRow(tool);
                row.PropertyChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(Access));
                    RecruitCommand.NotifyCanExecuteChanged();
                };
                Tools.Add(row);
            }
            Skills.Clear();
            try
            {
                foreach (var skill in await api.GetSkillsAsync(cancellationToken))
                    Skills.Add(new SkillChoice(skill) { IsSelected = current?.Skills?.Contains(skill.Key) == true });
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
            {
                // Sans la liste, on recrute sans compétence.
            }
            Models.Clear();
            Models.Add(new ModelChoice(null, "Modèle du poste"));
            try
            {
                foreach (var m in await api.GetModelChoicesAsync(cancellationToken))
                    Models.Add(new ModelChoice(new LlmSelectionDto(m.ConnectionId, m.Id), m.Free ? $"{m.Name} · gratuit" : m.Name));
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
            {
                // Sans la liste, l'agent garde le modèle du poste.
            }
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
    private void ToggleTool(ToolRow tool) => tool.IsOn = !tool.IsOn;

    [RelayCommand]
    private void PickRole(RoleChoice choice)
    {
        foreach (var r in Roles)
            r.IsSelected = r == choice;
        Role = choice;
        // Le poste remplit les réglages ; un poste « Personnalisé » part de la lecture seule.
        _baseTools = choice.IsCustomSlot ? ["list_files", "read_file"] : [.. choice.Agent.Tools.Select(t => t.Name)];
        foreach (var t in Tools)
            t.IsOn = _baseTools.Contains(t.Tool.Name);
        _baseInstructions = choice.Agent.Instructions;
        Instructions = choice.Agent.Instructions;
        Model = Models.FirstOrDefault(m => m.Selection is not null && m.Selection == choice.Agent.ModelOverride) ?? Models.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanRecruit))]
    private async Task RecruitAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Error = null;
        try
        {
            var role = Role!.Key;
            var tools = Tools.Where(t => t.IsOn).Select(t => t.Tool.Name).ToList();
            var instructions = Instructions.Trim();
            var model = Model?.Selection ?? Role.Agent.ModelOverride;
            if (Role.Agent.Mine && IsPersonalized)
            {
                // Un agent à soi : on le règle directement.
                await api.UpdateMyAgentAsync(role, new UpdateAgentProfileRequest(Role.Name, Role.Agent.Role, tools),
                    instructions.Length > 0 ? instructions : null, model, cancellationToken);
            }
            else if (IsPersonalized)
            {
                // Un poste partagé qu'on adapte, ou « Personnalisé » : un agent à soi, que personne d'autre ne voit.
                var name = Role.IsCustomSlot ? $"Agent de {Name.Trim()}" : $"{Role.Name} de {Name.Trim()}";
                var summary = Role.IsCustomSlot ? Summary(instructions) : Role.Agent.Role;
                var created = await api.CreateAgentAsync(new CreateAgentRequest(
                    name.Length > 40 ? name[..40].Trim() : name, summary.Length >= 5 ? summary : $"Agent personnel de {Name.Trim()}.",
                    instructions, tools, Personal: true, Model: model, BasedOn: Role.IsCustomSlot ? null : role), cancellationToken);
                role = created.Key;
            }

            await api.RecruitAsync(ProjectId, new RecruitRequest(
                role, Selected!.Portrait.Id, Name.Trim(), [.. Traits.Where(t => t.IsSelected).Select(t => t.Trait.Key)],
                [.. Skills.Where(s => s.IsSelected).Select(s => s.Skill.Key)]), cancellationToken);
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

    /// <summary>La première phrase de la consigne, 200 caractères au plus : sa description sur les cartes.</summary>
    private static string Summary(string instructions)
    {
        var end = instructions.IndexOfAny(['.', '\n']);
        var first = (end > 0 ? instructions[..(end + 1)] : instructions).Trim();
        return first.Length > 200 ? first[..199].TrimEnd() + "…" : first;
    }

    [RelayCommand]
    private void Cancel() => Done?.Invoke();

    [RelayCommand]
    private void WriteSkill()
    {
        IsWritingSkill = !IsWritingSkill;
        Error = null;
    }

    // Mêmes bornes que le serveur : un nom, une phrase « quand s'en servir », une procédure.
    private bool CanSaveSkill => SkillName.Trim().Length is >= 2 and <= 40 && SkillDescription.Trim().Length is >= 10 and <= 200
        && SkillBody.Trim().Length is >= 20 and <= 8000;

    /// <summary>Écrite ici, elle est à vous seul ; elle rejoint la liste, déjà cochée.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveSkill))]
    private async Task SaveSkillAsync(CancellationToken cancellationToken)
    {
        try
        {
            var skill = await api.CreateSkillAsync(
                new CreateSkillRequest(SkillName.Trim(), SkillDescription.Trim(), SkillBody.Trim()), cancellationToken);
            Skills.Add(new SkillChoice(skill) { IsSelected = true });
            (SkillName, SkillDescription, SkillBody, IsWritingSkill, Error) = ("", "", "", false, null);
        }
        catch (HttpRequestException ex)
        {
            Error = $"Compétence refusée : {ex.Message}";
        }
    }

    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}' \-]{0,23}$")]
    private static partial Regex NamePattern();
}
