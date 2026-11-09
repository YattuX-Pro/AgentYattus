using AgentYattus.Contracts;

namespace AgentYattus.Api.Modules.Agents;

internal static class RecruitmentAssistant
{
    public const string Tool = "preparer_recrutement";
    public static readonly AgentRole Role = new("_rh", "Agent RH", "Transforme un besoin en agent et en compétences concrètes.",
        "Tu es l'agent RH d'Agent Yattus, disponible depuis le recrutement. Tu aides à créer des agents logiciels, pas à recruter des humains. " +
        "L'utilisateur décrit avec ses mots ce qu'il voudrait déléguer : il n'a pas besoin de connaître les skills ni les prompts. " +
        "Comprends le résultat attendu, le contexte, le public, les tâches récurrentes, les limites et les critères d'un travail réussi. " +
        "Si une information indispensable manque, pose au plus trois questions courtes et concrètes à la fois, avec des exemples de réponses ; " +
        "attends sa réponse. N'impose pas de questionnaire si son besoin est déjà clair. Propose des choix simples aux personnes qui ne savent pas. " +
        "Quand tu as assez d'éléments, appelle preparer_recrutement avec une fiche complète : nom du poste, résumé, consigne et une à trois " +
        "compétences réutilisables, chacune avec quand l'utiliser et une procédure vérifiable. Les compétences ne doivent pas répéter toute la consigne. " +
        "Écris des instructions précises sur le rôle, les livrables, la méthode, les questions à poser, la vérification et les limites. " +
        "Choisis uniquement les outils nécessaires dans le catalogue donné. N'invente aucune intégration : pas d'envoi de mail, de navigation libre " +
        "ou d'accès à un compte si aucun outil ne le permet. Un agent sans besoin de fichiers peut utiliser read_documentation comme seul outil. " +
        "N'inclus aucun secret, clé ni donnée propre à la conversation dans une compétence réutilisable. " +
        "Tu prépares une fiche : n'affirme jamais avoir créé ou recruté l'agent. L'interface propose ensuite de le créer ou d'ajuster sa fiche. " +
        "Après l'outil, résume brièvement le profil et ses limites, en français. Si le besoin change, prépare une nouvelle fiche entière.", [Tool]);

    internal static string? Validate(RecruitmentDraftDto? draft)
    {
        if (draft is null || string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Length is < 2 or > 40
            || !draft.Name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-'))
            return "Le nom du poste doit faire de 2 à 40 caractères : lettres, chiffres, espaces, apostrophes et tirets.";
        if (draft.Summary is null || draft.Summary.Length is < 5 or > 200
            || draft.Instructions is null || draft.Instructions.Length is < 20 or > 8000)
            return "Le résumé doit faire de 5 à 200 caractères, la consigne de 20 à 8 000 caractères.";
        var known = AgentsModule.AllTools.Select(t => t.Name).ToHashSet();
        if (draft.Tools is not { Count: > 0 } || draft.Tools.Distinct().Count() != draft.Tools.Count || draft.Tools.Any(t => !known.Contains(t)))
            return "Choisis au moins un outil dans le catalogue, sans doublon ni outil inventé.";
        if (draft.Skills is not { Count: >= 1 and <= 3 } || draft.Skills.Any(s => s is null
            || SkillsEndpoints.Validate(s.Name ?? "", s.Description ?? "", s.Body ?? "").Count > 0))
            return "Prépare une à trois compétences valides : nom de 2 à 40 caractères, description de 10 à 200, procédure de 20 à 8 000.";
        if (SkillReview.Secrets(draft.Instructions + "\n" + string.Join('\n', draft.Skills.Select(s => s.Description + "\n" + s.Body))) is not null)
            return "Retire les secrets de la fiche et des compétences.";
        return null;
    }
}
