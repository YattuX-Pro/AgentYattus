# Leçon 107 — Mesurer les compétences

> **Commit** : `feat(skills): mesurer l'usage et la réussite de chaque compétence`
> **Phase** : les compétences
> **Fichiers** : `Contracts/Skills.cs`, `Api/Modules/Audit/AuditModule.cs`, `Api/Modules/Missions/MissionsModule.cs`, `Client.Core/{ApiClient,RecruitViewModel,ReportsViewModel}.cs`, `Desktop/Views/ReportsView.axaml`, tests

## 1. Ce qu'on construit

Une compétence qui ne sert jamais, ou qui accompagne surtout des échecs, doit se voir. On mesure deux choses :

- **Combien de fois** un agent l'a chargée (`Skill.Uses`, compté par `use_skill`, leçon 104).
- **Combien de missions** l'ont chargée, et **combien ont abouti** : `GET /skills/performance`.

Où le voir :

- **Nouveau collègue** : au survol d'une compétence, par exemple « Chargée 4 fois · 2 missions sur 3 abouties. ».
- **Rapports** : sous la réponse de l'agent, une pastille vert-de-gris « revue-perf · compétence utilisée ». C'est le pendant de la pastille laiton « REPORT.md · écrit avec votre accord ».

## 2. Le problème

Claude Code ne dit pas quels skills ont servi, ni s'ils ont aidé. Or une compétence est une hypothèse : « avec cette procédure, l'agent fera mieux ». Sans mesure, on ne sait pas si elle est vraie.

## 3. La décision

- **La source, c'est le journal.** `use_skill` y inscrit `competence:rôle:clé`. Comme pour les statistiques des agents (leçon 52), on compte ce qui a été exécuté, pas ce que l'agent raconte.
- **Le calcul est dans le module Missions.** C'est lui qui connaît le statut des missions. Il dépend déjà d'Agents (`IAgentCatalog.SkillKeysAsync`) et lit le journal (`AuditLog.EventsStartingWithAsync`).
- **Chacun voit ses chiffres.** Seulement les compétences que tu vois, et seulement tes missions ; l'administrateur voit toutes les missions. Une compétence de l'instance ne révèle donc pas l'activité des autres.
- **Une mission compte une fois**, même si elle a chargé trois fois la même compétence (`Distinct`).

## 4. Le code expliqué

```csharp
var loads = (await audit.EventsStartingWithAsync("competence:", ct))
    .Select(e => (e.MissionId, Skill: e.Type.Split(':', 3) is [_, _, var key] ? key : ""))
    .Where(e => visible.Contains(e.Skill)).Distinct().ToList();
```

`Split(':', 3) is [_, _, var key]` est un **motif de liste** (C# 11) : on prend le troisième morceau s'il y en a exactement trois.

## 5. Vérifie toi-même

- `dotnet test --filter The_agent_sees_only_the_index` vérifie que `/skills/performance` rend `(rapport-client, 1, 1)` pour l'administrateur, et rien pour Alice, qui n'a lancé aucune mission.
- `dotnet test --filter The_measure_says` vérifie les textes du survol et de la pastille.

## 6. Les limites

- **Corrélation n'est pas causalité.** Une mission qui réussit avec une compétence aurait peut-être réussi sans. Pour le savoir vraiment, il faut une **évaluation** : la même mission, avec et sans la compétence.
- **Le journal est lu en entier** à chaque appel (`ponytail` comme pour les agents). Une table agrégée viendra si le journal devient gros.
- **Purge** : la rétention du journal (admin › Réglages) efface aussi ces mesures. En revanche, `Uses` reste dans la table des compétences.

## 7. Quiz

1. Pourquoi compter depuis le journal plutôt que demander à l'agent quelles compétences il a utilisées ?
2. Pourquoi un non-administrateur ne compte-t-il que ses propres missions ?

<details>
<summary>Réponses du quiz</summary>

1. Le journal enregistre ce que le code a réellement exécuté. La réponse de l'agent peut se tromper, ou être influencée par un fichier lu.
2. Une compétence de l'instance est partagée : compter toutes les missions révélerait l'activité des autres utilisateurs.

</details>
