# Leçon 104 — Les compétences : un savoir-faire chargé seulement quand il sert

> **Commit** : `feat(agents): les compétences, chargées par l'agent quand sa tâche en a besoin`
> **Phase** : les compétences
> **Fichiers** : `Contracts/Skills.cs`, `Contracts/Team.cs`, `Api/Modules/Agents/{Skills,AgentRoles,AgentFrameworkRunner,AgentsModule,AgentDefinitions}.cs`, `Api/Modules/Team/{Colleagues,TeamDb}.cs`, `Missions/MissionWorker.cs`, migrations `Skills` et `ColleagueSkills`, `tests/SkillsTests.cs`

## 1. Le concept

Claude Code a des **skills** : un dossier avec un `SKILL.md` (un nom, une description, une procédure). Le modèle ne voit au départ que le nom et la description. Il ne charge la procédure que lorsque sa tâche y correspond. On appelle ça le **chargement progressif** (*progressive disclosure*).

Agent Yattus fait pareil avec des **compétences** :

| | Toujours dans la conversation ? | Sert à |
|---|---|---|
| Consigne | Oui | Qui est l'agent, ses règles |
| Outil | Sa signature seulement | Une action (lire, écrire) |
| **Compétence** | **Son nom et sa description seulement** | **Un savoir-faire** (une procédure, un modèle de document) |

## 2. Le problème

Mettre tout le savoir-faire dans la consigne a deux défauts :

- chaque mission paie tous les tokens, même ceux des procédures qui ne servent pas ;
- le modèle se perd dans des règles qui ne concernent pas sa tâche.

Avec les compétences, un agent peut en connaître beaucoup et ne payer que celle qui sert.

## 3. Les options

1. **Tout dans la consigne** : simple, mais coûteux et confus quand le savoir-faire grossit.
2. **Des fichiers dans le workspace** que l'agent lit avec `read_file` : il faudrait que l'utilisateur gère des fichiers. Or ce qui est écrit dans le workspace est une donnée, jamais une consigne (leçon 03).
3. **En base, rattachées au collègue, avec un outil dédié** : c'est notre choix.

## 4. La décision

- **Une compétence appartient à quelqu'un**, comme un agent (leçon 103) :
  - `OwnerId` null : une compétence de l'instance, pour tous ;
  - sinon, elle n'est visible que de son auteur.
- **L'administrateur peut cacher la procédure** (`Hidden`) : les agents la lisent, les utilisateurs non.
- **On la donne à un collègue, pas à un poste.** Elle se range avec le prénom et les traits, sur le siège du projet. Ajouter une compétence à l'Analyste partagé ne crée donc pas de copie de l'agent.

## 5. Le code expliqué

**Premier étage : l'index.** `AgentRoles.Compose` ajoute la liste des compétences avant les garde-fous, qui restent les derniers mots :

```csharp
custom ?? role.Instructions,     // la consigne
style,                           // « Tu t'appelles Nora… »
SkillsIndex(skills),             // « - rapport-client : Pour tout rapport… »
Guardrails(role, skills: true),  // toujours en dernier
```

**Deuxième étage : la procédure.** L'outil `use_skill` la lit en base au moment où l'agent la demande :

```csharp
if (!allowed.Contains(key) || ... is not { } skill)
    return $"Compétence inconnue. Tes compétences : {string.Join(", ", allowed)}.";
skill.Uses++;
await audit.AppendAsync(missionId, $"competence:{role}:{skill.Key}", ct);
return skill.Body;
```

`allowed` ne contient que les compétences de ce collègue. Le nom vient du modèle, qui peut en inventer un : la vérification se fait dans le code, jamais dans la consigne.

**Le trajet.** `MissionWorker` demande au module Team les **sièges** du projet (`SeatsAsync`). Chaque siège porte la présentation du collègue et les clés de ses compétences (`AgentSeat`). Le runner reçoit ces sièges, sans jamais lire la base d'un autre module.

## 6. Vérifie toi-même

- `dotnet test --filter SkillsTests` lance trois tests :
  - chacun ses compétences ;
  - l'index sans la procédure, puis la procédure chargée sur demande ;
  - une compétence que le collègue n'a pas reste fermée.
- **Cas d'échec** : Bob recrute avec la compétence d'Alice, il reçoit une 400. L'agent demande « secret », il reçoit « Compétence inconnue ».

## 7. Les limites

- **Le modèle doit penser à appeler `use_skill`.** Un petit modèle peut l'oublier. Des évaluations le mesureraient.
- **Pas de scripts.** Une compétence est du texte. Exécuter du code demanderait un bac à sable.
- **Pour l'instant, rien à l'écran.** Les compétences ne se gèrent que par l'API. L'écran « Nouveau collègue » vient dans le prochain commit.

## 8. Quiz

1. Pourquoi la procédure n'est-elle pas dans la consigne ?
2. Pourquoi rattacher la compétence au collègue plutôt qu'à l'agent ?

<details>
<summary>Réponses du quiz</summary>

1. Elle ne coûterait des tokens que si l'agent en a besoin : seuls le nom et la description sont payés à chaque mission.
2. Le collègue est propre à ton projet : tu ajoutes une compétence à l'Analyste partagé sans en faire une copie, et sans toucher l'équipe des autres.

</details>
