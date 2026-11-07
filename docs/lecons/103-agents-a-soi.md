# Leçon 103 — Chacun ses agents, sans espace admin

> **Commit** : `feat(agents): chaque utilisateur recrute et règle ses propres agents`
> **Phase** : le bureau des agents
> **Fichiers** : `Api/Modules/Agents/{AgentsModule,AgentRoles,AgentDefinitions}.cs`, migration `PersonalAgents`, `Api/Modules/Llm/LlmModule.cs`, `Api/Modules/Projects/ProjectsModule.cs`, `Contracts/{Agents,Llm}.cs`, `Client.Core/{ApiClient,RecruitViewModel,ShellViewModel}.cs`, `Desktop/Views/RecruitView.axaml`, admin `core/api.ts`, tests

## 1. Ce qu'on construit

L'espace admin est réservé au propriétaire de l'instance. Un utilisateur règle son équipe depuis le logiciel, sur l'écran « Nouveau collègue » de la maquette :

- **Visage, prénom, personnalité** : pour n'importe quel poste, y compris l'Analyste et le Rédacteur, avec le crayon de la fiche agent.
- **Consigne, outils, modèle** : modifiables directement. Si l'on s'écarte du poste, le recrutement crée un **agent à soi**, visible de son auteur seul.
- **Poste « Personnalisé »** : un agent écrit de zéro.
- **L'admin** fixe toujours l'équipe par défaut (visages, prénoms) et les agents partagés. Chaque utilisateur peut ensuite les remplacer chez lui.

## 2. Le bug : pas de projet, pas d'équipe

Le bouton « Recruter » et le crayon dépendent de l'équipe du projet personnel. Or ce projet n'était créé qu'à la **première mission**. Un utilisateur qui n'avait rien lancé n'avait donc ni équipe, ni bouton.

Correction à la source : `GET /projects` crée le projet personnel s'il manque. Tous les écrans en profitent, au lieu d'un correctif par écran.

## 3. Un agent appartient à quelqu'un

`CustomRole` gagne une colonne `OwnerId` : null pour un agent de l'instance, l'identifiant de l'utilisateur pour un agent à soi.

```csharp
.Where(r => (includeRetired || r.Active) && (everyone || r.OwnerId == null || r.OwnerId == mine))
```

Les droits se lisent dans une seule méthode :

```csharp
public async Task<bool> CanEditAsync(string key, CancellationToken ct) =>
    me.IsInstanceAdmin || (MeId is { } id && await OwnerOfAsync(key, ct) == id);
```

- **Bob** ne voit pas l'agent d'Alice, ne le modifie pas et ne lance aucune mission avec : le catalogue filtré ne le contient pas.
- **L'admin** voit tous les agents dans l'espace admin, grâce à `?all=true`.

## 4. Une consigne cachée reste cachée

Les consignes de l'Analyste et du Rédacteur peuvent contenir des règles internes : un utilisateur ne les lit pas. Pour l'adapter quand même, la consigne vide part avec `BasedOn: "analyste"`, et **le serveur** recopie la consigne du poste. Elle ne passe jamais par le client.

## 5. Ce qui reste

- Un Analyste adapté devient un agent à part (« Analyste de Nora ») sur son propre poste. L'Analyste partagé reste dans l'équipe.
- Les modèles proposés sont ceux des connexions de l'instance (`/llm/choices`). Un utilisateur ne branche pas sa propre clé.

## 6. Quiz

1. Pourquoi créer le projet personnel dans `GET /projects` plutôt que dans l'écran Agents ?
2. Comment un utilisateur adapte-t-il l'Analyste sans jamais lire sa consigne ?

<details>
<summary>Réponses du quiz</summary>

1. Tous les écrans passent par la liste des projets : une seule correction, là où tous les chemins se rejoignent.
2. Il envoie une consigne vide avec `BasedOn`, et le serveur recopie celle du poste.

</details>
