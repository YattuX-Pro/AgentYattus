# Leçon 01 — Le socle .NET 10

> **Commit** : `chore: socle .NET 10`
> **Phase** : 0, socle et boucle d'agent
> **Fichiers** : `global.json`, `AgentYattus.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `docs/roadmap.md`

## 1. Ce qu'on construit

Les **fondations** du dépôt : aucun programme encore, seulement les règles communes que tous les projets suivront.

> **Une image** : avant de construire des pièces, on coule la dalle et on fixe les normes du chantier. Le même ciment pour tous, les mêmes vis, les mêmes règles de sécurité.

## 2. Le problème

Un logiciel comme Agent Yattus contient une dizaine de projets : l'API, le desktop, le mobile, les tests… Sans règles communes :

- chaque projet peut viser une version différente de .NET ;
- deux projets peuvent utiliser deux versions différentes d'une même bibliothèque, et se contredire ;
- un collègue (ou la CI) compile avec un autre SDK et obtient un autre résultat ;
- les petits avertissements du compilateur s'accumulent jusqu'à masquer un vrai bug.

## 3. Les fichiers, un par un

### 3.1 `global.json` — quel SDK utiliser

```json
{
  "sdk": { "rollForward": "latestFeature", "version": "10.0.400" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

- Le **SDK** est la boîte à outils qui compile le code (`dotnet build`, `dotnet test`…).
- `version: 10.0.400` : on veut au moins cette version.
- `rollForward: latestFeature` : une version plus récente de la même famille 10.0 est acceptée.
- `test.runner` : les tests passent par **Microsoft.Testing.Platform**, le nouveau moteur de tests de .NET.

### 3.2 `AgentYattus.slnx` — la liste des projets

Une **solution** regroupe les projets pour qu'on les ouvre, compile et teste ensemble. `.slnx` est le nouveau format, en XML lisible, qui remplace l'ancien `.sln`. Au départ elle est vide : chaque commit y ajoutera son projet.

### 3.3 `Directory.Build.props` — les réglages de tous les projets

```xml
<TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

Ce fichier s'applique **automatiquement à tous les projets** du dossier et de ses sous-dossiers.

| Réglage | Effet |
|---|---|
| `TargetFramework` `net10.0` | Tous les projets visent .NET 10, une version LTS (supportée 3 ans). |
| `ImplicitUsings` | Les `using System;`, `using System.Linq;`… courants sont ajoutés tout seuls. |
| `Nullable` | Le compilateur signale les risques de `NullReferenceException`, l'erreur la plus fréquente en C#. |
| `TreatWarningsAsErrors` | **Un avertissement bloque la compilation.** On corrige tout de suite, au lieu d'accumuler. |

### 3.4 `Directory.Packages.props` — les versions des bibliothèques

```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
...
<PackageVersion Include="xunit.v3" Version="4.0.2" />
```

C'est la **gestion centralisée des paquets** (*Central Package Management*). Les versions sont écrites **une seule fois**, ici. Les projets disent seulement *quelle* bibliothèque ils veulent, sans numéro de version. Impossible, donc, d'avoir deux versions de la même bibliothèque dans la solution.

### 3.5 `.gitignore` — ce que Git ne doit pas suivre

Les dossiers générés à la compilation (`bin/`, `obj/`), les fichiers propres à un éditeur, les fichiers `.env`… Ils sont recréés à chaque build, ou personnels, ou secrets : ils n'ont rien à faire dans l'historique.

### 3.6 `docs/roadmap.md` — la feuille de route

Les phases et les commits prévus, du socle jusqu'au MVP.

## 4. Les options écartées

| Option | Pourquoi non |
|---|---|
| Écrire la version de chaque paquet dans chaque projet | Les versions finissent par diverger. |
| Laisser les avertissements | Ils s'accumulent et cachent les vrais problèmes. |
| Ne pas fixer le SDK | La CI et ta machine pourraient compiler différemment. |

## 5. Vérifie toi-même

```bash
dotnet --version
```

Le numéro affiché doit commencer par `10.0.4` ou plus.

## 6. Quiz

1. Où écrit-on la version d'une bibliothèque, et pourquoi à un seul endroit ?
2. Que se passe-t-il si le code produit un avertissement ?
3. À quoi sert `global.json` ?

## 7. Pour aller plus loin

- Nouveautés de .NET 10 : <https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview>
- Central Package Management : <https://learn.microsoft.com/nuget/consume-packages/central-package-management>

<details>
<summary>Réponses du quiz</summary>

1. Dans `Directory.Packages.props`. Un seul endroit, pour que tous les projets utilisent forcément la même version.
2. La compilation échoue, à cause de `TreatWarningsAsErrors`.
3. À fixer la version du SDK .NET, et le moteur de tests, pour que tout le monde compile de la même façon.

</details>
