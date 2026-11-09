# Leçon 132 — Des compétences versionnées, validées et choisies pour une mission

> **Commit** : `feat(skills): versions, brouillon à valider, import SKILL.md, compétences choisies pour une mission`
> **Phase** : compétences et documentation · **ADR** : 0017
> **Fichiers** : `Contracts/{Skills,Missions}.cs`, `Api/Modules/Agents/{Skills,SkillFile,AgentDefinitions,AgentRoles,AgentFrameworkRunner}.cs` + migration `SkillVersionsAndSources`, `Api/Modules/Missions/{Mission,MissionsModule,MissionWorker}.cs` + migration `MissionSkills`, `Client.Core/{ApiClient,HomeViewModel,RecruitViewModel}.cs`, `Desktop/Views/HomeView.axaml`, `tests/SkillGovernanceTests.cs`

## 1. Ce qu'on construit

- **Versions** : modifier une compétence crée la version suivante ; l'historique se lit (`GET /skills/{clé}/versions`), et une ancienne revient (`POST …/versions/{n}/restore`, qui crée une nouvelle version).
- **Statut** : *brouillon* (aucun agent ne la lit), *approuvée*, *dépréciée* (encore lue, signalée).
- **Import d'un `SKILL.md`** (`POST /skills/import`) : il arrive en brouillon, avec ce qui mérite une relecture.
- **Références** : fichiers du dépôt ou pages officielles, dix au plus.
- **Choisir des compétences pour une mission** : à l'accueil, le bouton « Compétences ». Elles sont données **en entier** à chaque agent de la mission ; leur version entre dans le journal.

Le format d'un `SKILL.md` :

```markdown
---
name: Conventions du projet
description: Pour toute modification du code de ce dépôt.
technologies:
  - .NET 10
tools:
  - edit_file
references:
  - depot: AGENTS.md
  - https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions?view=net-10.0
---
1. Lis AGENTS.md avant de modifier.
2. …
```

`name` et `description` sont ceux du format « Agent Skills » d'Anthropic. `references`, `technologies` et `tools` sont à nous.

## 2. Le problème

Une compétence modifiée en place change ce que font les missions sans qu'on puisse dire laquelle a suivi quoi. Une compétence importée telle quelle peut contenir un secret ou une consigne dangereuse. Et la seule façon d'en donner une à un agent était de l'attacher à un collègue.

## 3. Les options

Voir l'ADR 0017. Choix : versions et brouillons dans la base, import texte seul, choix explicite par mission, chargement progressif conservé pour les compétences du collègue.

## 4. Le code expliqué

- **`SkillLibrary.NewVersionAsync`** : la version d'avant entre dans `SkillVersions` si elle n'y est pas (les compétences d'avant cette leçon), puis la nouvelle.
- **`SkillStatus`** commence par `Approved`. EF n'écrit pas une valeur égale à celle du type par défaut (0) : il prend celle de la base. Avec `Draft` en premier, une compétence importée en brouillon était enregistrée approuvée. Le test l'a montré.
- **`SkillFile.Parse`** lit un sous-ensemble de YAML : `clé: valeur` et des listes `- élément`. Rien d'autre n'est interprété, rien n'est exécuté.
- **`SkillReview`** :
  - un **secret** (clé privée, `AKIA…`, `sk-…`, `ghp_…`, `mot de passe : …`) est refusé ;
  - des **consignes risquées** sont signalées : ignorer des règles, agir sans accord, `curl … | sh`, `.env`.
- **`AgentRoles.ChosenSkills`** ajoute les compétences choisies après la présentation du collègue et avant les garde-fous. Elles arrivent avec la **règle de conflit** : la demande de l'humain, puis ces compétences, puis celles du collègue ; jamais avant la sécurité ; l'agent signale le conflit.
- **Le journal** : `competence:choisie:clé` (compté par la mesure de la leçon 107) et `version:competence:clé:n`.
- **Les droits** : une compétence à soi est invisible des autres (404) ; on ne choisit que les siennes ou celles de l'instance, approuvées. Une relance garde celles que l'on peut encore utiliser.
- **Le bureau** :
  - le bouton « Compétences » liste celles qu'on peut choisir (avec leur version) ;
  - « Importer un SKILL.md… » montre **tout** le brouillon (procédure, références, avertissements, portée) avant « Valider ».

## 5. Vérifie toi-même

```bash
dotnet test tests/AgentYattus.Api.Tests --filter-class "*SkillGovernanceTests"
```

```bash
dotnet test tests/AgentYattus.Client.Core.Tests --filter-class "*HomeViewModelTests"
```

## 6. Les limites

- Pas de compétences **de projet** ni **d'organisation** : personnelles ou de l'instance.
- Pas de **sélection automatique** au-delà de l'index léger : l'agent choisit parmi celles de son collègue, l'humain choisit pour la mission.
- Pas d'import des **ressources** d'un dossier (modèles, exemples) : le texte du `SKILL.md` seulement.
- « Cachée » n'est pas « secrète » : le modèle lit la procédure, il peut la répéter.
- Une compétence **déjà approuvée** n'est pas relue par `SkillReview` quand on la modifie : seul le secret est vérifié.
- La mesure reste le nombre de chargements et la réussite des missions. Comparer **avec et sans** compétence demande les évaluations sur de vrais modèles, pas encore écrites pour ce cas.

## 7. Quiz

1. Pourquoi restaurer une version en crée-t-il une nouvelle, au lieu de revenir en arrière ?
2. Pourquoi `tools` dans un `SKILL.md` n'accorde-t-il rien ?

<details><summary>Réponses</summary>

1. L'historique ne s'efface jamais : une mission qui a suivi la version 2 doit pouvoir la relire, même si l'on revient à la 1.
2. Un fichier importé vient de n'importe où. Les outils sont du code revu, attribués par rôle : une compétence guide la méthode, elle ne donne pas de droit.

</details>
