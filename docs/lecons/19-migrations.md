# Leçon 19 — Les migrations EF Core, et la réponse de l'agent enfin enregistrée

> **Commit** : `chore(db): migrations EF Core`
> **Phase** : 2, backend
> **Fichiers** : `src/AgentYattus.Api/Database.cs`, `Modules/Missions/Migrations/`, `Modules/Audit/Migrations/`, `Modules/Missions/Mission.cs`, `Modules/Agents/` (`AgentsModule.cs`, `AgentFrameworkRunner.cs`), `Modules/Missions/MissionWorker.cs`, `src/AgentYattus.Contracts/Missions.cs`, `dotnet-tools.json`, les tests

## 1. Ce qu'on construit

Deux choses liées :

1. **Les migrations** : une façon contrôlée de faire **évoluer** la structure de la base de données.
2. **Le premier changement de structure** : une colonne `Result`, qui enregistre la réponse de l'agent avec la mission. Jusqu'ici, cette réponse n'était écrite que dans les journaux (leçon 17).

## 2. Le problème

Au démarrage, l'API créait les tables **si elles n'existaient pas**. Mais si la table `Missions` existe déjà, sans la colonne `Result` ? Rien ne se passe : la table reste telle quelle, et le code plante en cherchant une colonne absente.

Il faut un moyen de dire à la base : « passe de la version 1 à la version 2 de ma structure », **sans perdre les données**.

> **Une image** : les migrations sont les **plans de travaux successifs** d'une maison habitée. On n'abat pas la maison pour ajouter une fenêtre : on suit le plan « ajouter une fenêtre ». Et chaque maison garde la liste des travaux déjà faits.

## 3. Comment ça marche

### 3.1 Une migration = un fichier C#

```bash
dotnet ef migrations add AddMissionResult --context MissionsDb --output-dir Modules/Missions/Migrations
```

L'outil `dotnet ef` compare **le modèle C# actuel** (les classes et le `DbContext`) au **dernier état connu** (le fichier `MissionsDbModelSnapshot.cs`). Il écrit la différence sous forme de migration :

```csharp
public partial class AddMissionResult : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Result", schema: "missions", table: "Missions", type: "text", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Result", schema: "missions", table: "Missions");
}
```

- `Up` : appliquer le changement.
- `Down` : l'annuler.
- Le fichier **se relit** et **se versionne** dans Git : un relecteur voit exactement ce qui va changer en base.

Ce commit contient trois migrations :

| Module | Migration | Effet |
|---|---|---|
| Missions | `InitialCreate` | Crée le schéma `missions` et la table `Missions`, comme avant |
| Missions | `AddMissionResult` | Ajoute la colonne `Result` |
| Audit | `InitialCreate` | Crée le schéma `audit` et la table `Events` |

### 3.2 La base se souvient des travaux faits

Chaque base a une table **`__EFMigrationsHistory`** qui liste les migrations déjà appliquées. Au démarrage, EF Core n'applique **que celles qui manquent**. Lancer l'API deux fois de suite ne refait donc rien la deuxième fois.

```csharp
npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema)
```

Chaque module garde **son propre** historique dans son schéma. Les migrations du module Audit ne se mélangent pas à celles du module Missions : c'est la même idée d'isolation que dans l'ADR 0003.

### 3.3 Les appliquer au démarrage

```csharp
if (db.Database.IsNpgsql())
{
    await db.Database.MigrateAsync(cancellationToken);
    return;
}
```

Sur PostgreSQL, l'API applique les migrations manquantes au démarrage.

**Et SQLite ?** Les migrations générées sont écrites **pour PostgreSQL** (types `uuid`, `timestamp with time zone`…). Comme le prévoit l'ADR 0004, SQLite aura sa propre série de migrations, avec le mode local. En attendant, SQLite (les tests) crée encore ses tables directement :

```csharp
// ponytail: SQLite (tests, futur mode local) crée encore ses tables sans migrations ;
// sa propre série de migrations viendra avec le mode local (ADR 0004).
```

### 3.4 L'outil, déclaré dans le dépôt

```json
// dotnet-tools.json
"dotnet-ef": { "version": "10.0.14" }
```

`dotnet-ef` est installé comme **outil local** : sa version est écrite dans le dépôt. `dotnet tool restore` installe exactement la même version pour tout le monde.

Le paquet `Microsoft.EntityFrameworkCore.Design` sert à cet outil. Il est marqué `PrivateAssets="all"` : il ne part pas avec l'application publiée.

## 4. La réponse de l'agent, enregistrée

```csharp
public interface IAgentRunner
{
    Task<string?> RunAsync(Guid missionId, string goal, CancellationToken cancellationToken);
}
```

L'exécuteur **renvoie** maintenant la réponse. Le module Missions l'enregistre :

```csharp
mission.Result = await agent.RunAsync(mission.Id, mission.Goal, cancellationToken);
```

Et elle part vers les clients dans `MissionDto` :

```csharp
public sealed record MissionDto(Guid Id, string Goal, MissionStatus Status, DateTimeOffset CreatedAt, string? Result = null);
```

`string? Result = null` : un paramètre **optionnel**, ajouté **à la fin**. Le code déjà écrit qui crée des `MissionDto` à 4 arguments continue de compiler. C'est une évolution **compatible** du contrat.

### Les journaux ne contiennent plus la réponse

```csharp
// Seulement des métadonnées : la réponse peut citer le contenu des fichiers lus, elle reste en base.
logger.LogInformation("Mission {MissionId} : {Tokens} tokens, réponse de {Length} caractères", ...);
```

La réponse d'un agent peut **citer des fichiers** : un mot de passe trouvé dans un fichier de configuration, par exemple. Les journaux, eux, sont souvent copiés, envoyés vers des outils de supervision, lus par d'autres personnes. On n'y écrit donc que des **métadonnées** (nombre de tokens, longueur). C'est une revue de sécurité automatique qui l'a signalé.

## 5. Vérifications

| Vérification | Résultat |
|---|---|
| Les tests (SQLite) | 18 sur 18 ; ils vérifient que `Result` est rempli |
| `dotnet ef migrations has-pending-model-changes` | « No changes » : les migrations correspondent au modèle |
| Un **vrai PostgreSQL** jetable | Les 3 migrations s'appliquent ; 2ᵉ démarrage : rien n'est rejoué ; une mission est traitée normalement |

**Bruit connu dans les journaux** : au tout premier démarrage, EF Core essaie de lire `__EFMigrationsHistory` **avant** de la créer, et journalise un `Failed executing DbCommand`. Ce n'est pas une erreur : il crée la table juste après et applique les migrations.

## 6. ⚠️ Si tu as déjà une base de développement

Les tables créées **avant** ce commit n'ont pas d'historique de migrations. EF Core essaierait de les recréer, et échouerait. Une seule fois, repars d'une base vide (les missions de test sont perdues) :

```bash
docker compose down -v
```

```bash
docker compose up -d
```

## 7. Vérifie toi-même

```bash
dotnet tool restore
```

```bash
dotnet ef migrations list --project src/AgentYattus.Api --context MissionsDb
```

## 8. Quiz

1. Pourquoi « créer les tables si elles n'existent pas » ne suffit-il plus ?
2. À quoi sert la table `__EFMigrationsHistory` ?
3. Pourquoi les migrations générées ne servent-elles pas pour SQLite ?
4. Pourquoi ajouter `Result` en paramètre optionnel, à la fin de `MissionDto` ?
5. Pourquoi ne plus écrire la réponse de l'agent dans les journaux ?

## 9. Pour aller plus loin

- Les migrations EF Core : <https://learn.microsoft.com/ef/core/managing-schemas/migrations/>
- Appliquer les migrations : <https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying>
- Migrations et plusieurs fournisseurs : <https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers>
- Outils locaux .NET : <https://learn.microsoft.com/dotnet/core/tools/local-tools-how-to-use>

<details>
<summary>Réponses du quiz</summary>

1. Parce qu'une table qui existe déjà n'est pas modifiée : la nouvelle colonne n'y serait jamais ajoutée.
2. À savoir quelles migrations ont déjà été appliquées, pour n'appliquer que les manquantes.
3. Parce qu'elles utilisent des types propres à PostgreSQL. SQLite aura sa propre série, avec le mode local.
4. Pour que le code existant qui crée des `MissionDto` à 4 arguments continue de compiler : l'évolution est compatible.
5. Parce qu'elle peut citer des secrets lus dans des fichiers, et que les journaux circulent beaucoup plus que la base.

</details>
