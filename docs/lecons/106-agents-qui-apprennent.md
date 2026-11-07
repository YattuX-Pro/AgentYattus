# Leçon 106 — Des agents qui apprennent, avec ton accord

> **Commit** : `feat(agents): un agent propose une compétence, retenue seulement avec ton accord`
> **Phase** : les compétences
> **Fichiers** : `Api/Modules/Agents/{AgentFrameworkRunner,AgentRoles,AgentsModule,Skills}.cs`, `Api/Modules/Team/{Colleagues,TeamContent}.cs`, `Missions/MissionWorker.cs`, `Client.Core/{Scene,MissionViewModel,OfficeViewModel}.cs`, `Desktop/Views/OfficeView.axaml`, tests

## 1. Le concept

Dans Claude Code, un skill est écrit par une personne : le modèle ne crée jamais un skill tout seul. Ici, l'agent peut **proposer** une compétence à la fin d'une mission. Par exemple : « Tu m'as demandé de vouvoyer le client et de faire court : je le note ? ».

Rien n'est retenu sans toi. C'est le même mécanisme que pour l'écriture d'un fichier (leçon 26) : l'humain dans la boucle.

## 2. Le problème

Sans mémoire, tu répètes les mêmes consignes à chaque mission (« vouvoie », « trois phrases »…). Mais une mémoire automatique serait dangereuse : un fichier piégé du workspace pourrait faire « apprendre » à l'agent une mauvaise règle. Celle-ci reviendrait ensuite dans toutes les missions.

## 3. Les options

1. **Mémoire automatique** : l'agent écrit ce qu'il veut. C'est rapide, mais une injection de prompt devient permanente.
2. **Rien** : c'est sûr, mais tu te répètes.
3. **Une proposition, approuvée par un humain** : c'est notre choix. Le coût, c'est un clic.

## 4. La décision

- **Un outil `propose_skill(name, description, body)`**, enveloppé dans `ApprovalRequiredAIFunction`. La boucle s'arrête avant de l'exécuter, exactement comme pour `write_file`.
- **Qui le reçoit** : un agent qui a un collègue dans le projet, sur une mission lancée par quelqu'un. Il faut un siège où ranger la compétence, et un propriétaire.
- **Approuvée** : la compétence appartient à qui a lancé la mission, et rejoint le collègue qui l'a proposée.
- **Refusée** : la fonction n'est jamais appelée, donc rien n'est écrit.

## 5. Le code expliqué

**Côté Agents.** Approuvée, la fonction enregistre la compétence et la note dans `_learned` :

```csharp
var skill = await SkillLibrary.AddAsync(db, n, d, b, owner, hidden: false, by: $"agent:{role}", ct);
_learned.Add(new AgentLearned(role, skill.Key));
```

**Le retour.** Le runner rend ces compétences dans `AgentOutcome.Learned`. Le module Agents ne touche jamais la base du module Team : c'est `MissionRunner` qui fait le lien.

```csharp
foreach (var learned in outcome.Learned ?? [])
    await team.AttachSkillAsync(learnedIn, learned.Role, learned.Skill, limit.Token);
```

**Dans l'open space :**

- L'agent qui a proposé frappe à la porte, avec un texte qui dit ce qu'il veut : « Il veut retenir une compétence pour la prochaine fois ». Ce n'est plus toujours le rédacteur.
- La relecture montre le nom, « Quand : … », puis toute la procédure. Comme pour un fichier, « Approuver » attend que tout ait été affiché.
- Trois nouvelles répliques, modifiables dans l'admin comme les autres : `outil:use_skill`, `competence:proposee`, `competence:retenue`.

## 6. Vérifie toi-même

- `dotnet test --filter "An_agent_proposes_a_skill|A_refused_skill"`.
- **Cas d'échec** : refuser ne crée ni compétence, ni lien avec le collègue. Une proposition invalide (procédure trop courte), même approuvée, renvoie un refus au modèle sans rien écrire.

## 7. Les limites

- **Ton accord est la seule barrière.** Une procédure piégée, une fois approuvée, revient dans les missions suivantes. Relis-la vraiment.
- **Une procédure cachée peut ressortir.** L'agent qui a lu une compétence cachée pourrait la recopier dans sa proposition. Tu la verrais alors dans la relecture. Comme pour une consigne cachée : pas de vrai secret dedans.
- **On ne retient pas encore « une mission réussie grâce à telle compétence ».** C'est la mesure du prochain commit.

## 8. Quiz

1. Pourquoi `propose_skill` passe-t-il par l'accord humain, alors que `use_skill` non ?
2. Pourquoi le runner rend-il `Learned` au lieu de ranger lui-même la compétence sur le collègue ?

<details>
<summary>Réponses du quiz</summary>

1. `use_skill` lit ce que tu as déjà accepté. `propose_skill` écrit une règle qui reviendra dans toutes les missions suivantes : c'est une action à effet durable.
2. Le collègue vit dans le module Team. Le module Agents ne lit ni n'écrit sa base : il rend l'information, et `MissionRunner`, qui connaît les deux, fait le lien.

</details>
