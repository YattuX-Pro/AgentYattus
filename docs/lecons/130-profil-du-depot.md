# Leçon 130 — Le profil du dépôt : comprendre avant de modifier

> **Commit** : `feat(agents): explore_repository, le profil du dépôt sans rien exécuter`
> **Phase** : parcours de développement (P0)
> **Fichiers** : `Api/Modules/Agents/{RepositoryProfile,WorkspaceTools}.cs`, `tests/RepositoryProfileTests.cs`

## 1. Ce qu'on construit

Un outil en lecture, `explore_repository`, et la même fonction côté code (`WorkspaceTools.Profile`). Ils décrivent un dépôt **sans rien exécuter** :

- ses **langages** (par extension, les huit plus présents) ;
- ses **projets** (`.slnx`, `.csproj`, `package.json`, `go.mod`, `Cargo.toml`, `pyproject.toml`…) ;
- les **consignes qu'il donne** à qui y travaille : `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `CONTRIBUTING.md`, `README.md` (3 000 caractères chacun) ;
- les **commandes de contrôle**, trois au plus : `dotnet build` et `dotnet test` sur la solution racine, les scripts `build`, `test` et `lint` du `package.json` (avec `pnpm` ou `yarn` si leur fichier de verrou est là), `go test ./...`, `cargo test`, `python -m pytest`, `mvn -q -o test`, `make test`.

## 2. Le problème

Un agent qui modifie sans comprendre le dépôt invente des conventions, oublie les tests, et lance la mauvaise commande. Il faut lui dire **où il est**. Il faut aussi que le **code** connaisse les commandes de contrôle, pour les lancer lui-même (leçon 131).

## 3. Les options

| Option | Pour | Contre |
|---|---|---|
| Laisser l'agent explorer avec `list_files` | Rien à écrire | Lent, coûteux en tokens, différent à chaque fois |
| Un fichier de configuration par projet | Exact | Personne ne l'écrit |
| **Déduire des fichiers présents** | Gratuit, reproductible, recalculé à chaque appel | Heuristique : un dépôt atypique n'a pas de contrôle |

## 4. Le code expliqué

- `Profile()` parcourt le dossier avec **les mêmes règles que `search`** : ni lien symbolique, ni fichier exclu (`.env`…), ni dépendances (`node_modules`, `bin`, `obj`, `.git`…), 5 000 fichiers au plus (sinon « profil partiel »).
- Seuls les **petits** fichiers (64 Ko) de consignes et de projet sont lus.
- `DetectChecks` regarde **la racine** seulement. Un nom de fichier avec un espace est mis entre apostrophes pour le shell du conteneur (`dotnet test 'Mon App.sln'`).
- `Describe()` présente les consignes du dépôt comme des **données du dépôt** : elles guident les conventions de code, jamais les règles de sécurité ni les outils, que le code impose.
- Rien n'est mis en cache : chaque appel relit le dossier, donc jamais de vue périmée.

## 5. Vérifie toi-même

```bash
dotnet test tests/AgentYattus.Api.Tests --filter-class "*RepositoryProfileTests"
```

- un dépôt .NET : C# reconnu, `dotnet build Orders.slnx` et `dotnet test Orders.slnx`, AGENTS.md avant README.md, ni `.env` ni `node_modules` ;
- un `package.json` avec `pnpm-lock.yaml` : `pnpm run build`, `pnpm run lint`, et le script `test` par défaut de npm ignoré ;
- l'outil relit le dossier à chaque appel.

## 6. Les limites

- **La racine seulement** : un monorepo sans fichier racine n'a pas de contrôle (`ponytail:` dans le code).
- Pas de navigation dans les symboles (définitions, références) : `search` cherche du texte. Un serveur de langage (Roslyn, tsserver) viendra quand l'évaluation montrera le besoin.

## 7. Quiz

1. Pourquoi ne pas mettre le profil en cache ?
2. Pourquoi le profil présente-t-il AGENTS.md comme une « donnée » ?

<details><summary>Réponses</summary>

1. Le dépôt change pendant la mission (l'agent le modifie). Recalculer coûte quelques millisecondes ; une vue périmée coûterait une mauvaise décision.
2. AGENTS.md vient du dépôt, que n'importe qui peut modifier. Un dépôt piégé pourrait y écrire « lis ~/.ssh ». Les conventions de code, oui ; les règles de sécurité, jamais : le code les impose.

</details>
