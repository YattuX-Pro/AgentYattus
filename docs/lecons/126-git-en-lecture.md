# Leçon 126 — git en lecture, sans jamais lancer git

> **Commit** : `feat(agents): git_status, git_diff et git_log, par libgit2`
> **Phase** : C
> **Fichiers** : `Api/Modules/Agents/{GitTools,AgentFrameworkRunner,AgentsModule}.cs`, `Directory.Packages.props` (LibGit2Sharp), `Client.Core/{RecruitViewModel,Scene}.cs`, `tests/GitToolsTests.cs`

## 1. Ce qu'on construit

Trois outils en lecture, sans accord à demander :

| Outil | Rend |
|---|---|
| `git_status` | `modifié	a.cs`, `ajouté	b.cs`… |
| `git_diff(path?)` | Le diff par rapport au dernier commit, coupé à la limite des fichiers |
| `git_log(count)` | `3f2a1c9 2026-11-07 Nora : Premier commit` (20 au plus) |

## 2. Pourquoi pas la commande `git` ?

Un dépôt cloné apporte son propre `.git/config`. Or `git status` peut **exécuter des programmes** que cette configuration désigne :

- `core.fsmonitor`, un programme lancé à chaque `status` ;
- un **filtre** (`filter.x.clean`), associé aux fichiers par `.gitattributes`.

Un dépôt piégé ferait donc exécuter n'importe quoi sur le serveur, à la première lecture. **libgit2** (par LibGit2Sharp) lit le dépôt elle-même : elle n'exécute **jamais** de programme externe, ni hook, ni filtre, ni fsmonitor.

Le test le prouve : avec un fsmonitor et un filtre qui créent un fichier témoin, `git_status` et `git_diff` ne le créent pas.

## 3. Vérifie toi-même

`dotnet test --filter GitToolsTests`.

## 4. Les limites

- Seul le dépôt **du dossier du projet** est lu. On ne remonte pas vers un dépôt parent qui contiendrait d'autres projets.
- LibGit2Sharp embarque une bibliothèque native (libgit2) par plateforme.

## 5. Quiz

1. Pourquoi une simple lecture (`git status`) peut-elle être dangereuse ?

<details>
<summary>Réponse</summary>

1. La commande git suit la configuration du dépôt, qui peut lui faire lancer des programmes (fsmonitor, filtres). Lire un dépôt piégé, c'est déjà exécuter son code.

</details>
