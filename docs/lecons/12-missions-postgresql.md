# Leçon 12 — Créer et lire une mission, avec PostgreSQL

> **Commit** : `feat(missions): PostgreSQL, créer et lire une mission`
> **Phase** : 2, backend
> **Fichiers** : `src/AgentYattus.Api/Database.cs`, `Modules/Missions/Mission.cs`, `Modules/Missions/MissionsModule.cs`, `docker-compose.yml`, `appsettings.json`, `tests/AgentYattus.Api.Tests/ApiFactory.cs`, `MissionsTests.cs`

## 1. Ce qu'on construit

Les deux premières routes du module **Missions** :

- `POST /missions` : enregistrer une nouvelle mission ;
- `GET /missions/{id}` : relire une mission.

Les missions sont stockées dans **PostgreSQL**, lancé avec Docker.

## 2. Le problème

Une mission doit **survivre** à la fermeture de l'application : on veut pouvoir la relire demain. Il faut donc une base de données, et un moyen simple de lui parler depuis C#.

## 3. Les briques

### 3.1 PostgreSQL dans Docker — `docker-compose.yml`

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_HOST_AUTH_METHOD: trust
    ports:
      - "127.0.0.1:${POSTGRES_PORT:-5432}:5432"
```

```bash
docker compose up -d
```

- **Docker** fait tourner PostgreSQL dans un conteneur, sans rien installer sur ta machine.
- `trust` : pas de mot de passe. C'est acceptable **uniquement** parce que le port n'est ouvert que sur `127.0.0.1`. **Jamais** en production.
- `${POSTGRES_PORT:-5432}` : le port 5432 par défaut, modifiable s'il est déjà pris.

### 3.2 EF Core : parler à la base en C#

**EF Core** (*Entity Framework Core*) est un **ORM** : il traduit des classes C# en tables, et des requêtes C# en SQL. On n'écrit pas de SQL à la main.

```csharp
internal sealed class Mission
{
    public Guid Id { get; set; }
    public required string Goal { get; set; }
    public MissionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

Cette classe devient la table `Missions`. Chaque propriété devient une colonne.

```csharp
internal sealed class MissionsDb(DbContextOptions<MissionsDb> options) : DbContext(options)
{
    public DbSet<Mission> Missions => Set<Mission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("missions");
        modelBuilder.Entity<Mission>().Property(m => m.Status).HasConversion<string>();
    }
}
```

- Le **`DbContext`** représente la connexion à la base et ses tables.
- `HasDefaultSchema("missions")` : les tables du module sont rangées dans le schéma `missions`, leur propre « dossier » dans la base (voir l'ADR 0003).
- `HasConversion<string>()` : le statut est stocké en texte (`Running`), lisible directement dans la base.

### 3.3 `Database.cs` : brancher un module sur la base

```csharp
public static IServiceCollection AddModuleDb<TDb>(this IServiceCollection services) where TDb : DbContext
```

Une petite méthode réutilisable : elle enregistre le `DbContext` d'un module avec la chaîne de connexion `AgentYattus`, et crée ses tables au démarrage.

```csharp
// ponytail: pas de migrations, le schéma n'évolue pas ; passer aux migrations EF dès qu'une table change.
```

Pour l'instant, on crée les tables au démarrage si elles n'existent pas. Quand une table devra changer, on passera aux **migrations** (commit 19).

## 4. Les routes

```csharp
var missions = app.MapGroup("/missions");
missions.MapPost("/", CreateAsync);
missions.MapGet("/{id:guid}", GetAsync);
```

`MapGroup` regroupe les routes sous le même préfixe. `{id:guid}` n'accepte qu'un identifiant au bon format.

### Créer : valider, enregistrer, répondre 202

```csharp
if (string.IsNullOrWhiteSpace(request.Goal) || request.Goal.Length > MaxGoalLength)
    return TypedResults.ValidationProblem(...);   // 400

var mission = new Mission { Id = Guid.CreateVersion7(), Goal = request.Goal.Trim(), Status = MissionStatus.Pending, ... };
db.Missions.Add(mission);
await db.SaveChangesAsync(cancellationToken);
return TypedResults.Accepted($"/missions/{mission.Id}", mission.ToDto());   // 202
```

- **On valide toujours ce qui arrive de l'extérieur** : un but vide ou de plus de 4 000 caractères est refusé avec un **400**.
- `Guid.CreateVersion7()` : un identifiant unique **ordonné dans le temps**, plus efficace pour les index de la base qu'un identifiant purement aléatoire.
- **202 Accepted** + l'adresse de la mission : « bien reçu, voici où suivre son avancement ». Dans ce commit, la mission reste en `Pending` : son exécution arrive au commit suivant.
- `TypedResults` + `Results<Accepted<MissionDto>, ValidationProblem>` : le compilateur **connaît** les réponses possibles. Elles apparaissent aussi dans la documentation de l'API.

### Lire : 200 ou 404

```csharp
await db.Missions.FindAsync([id], cancellationToken) is { } mission
    ? TypedResults.Ok(mission.ToDto())
    : TypedResults.NotFound();
```

On ne renvoie **jamais** l'entité de base directement : on la convertit en `MissionDto`. La base peut évoluer sans casser les clients.

## 5. Les tests, sans Docker

```csharp
services.AddDbContext<TDb>(o => o.UseSqlite($"Data Source={_dbPath}"));
```

Dans les tests, `ApiFactory` remplace PostgreSQL par **SQLite**, sur un fichier temporaire. Les tests tournent partout, même sans Docker, et c'est aussi la preuve que le code marche sur les deux bases (ADR 0004).

| Test | Vérifie |
|---|---|
| `Post_returns_202_and_the_mission_can_be_read` | 202, l'adresse dans l'en-tête `Location`, et la relecture |
| `Post_with_empty_goal_returns_400` | un but vide est refusé |
| `Get_of_an_unknown_mission_returns_404` | une mission inconnue donne 404 |

## 6. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

Avec la vraie base :

```bash
docker compose up -d
dotnet run --project src/AgentYattus.Api --launch-profile http
curl -i -X POST http://localhost:5080/missions -H "Content-Type: application/json" -d '{"goal":"Relire le README"}'
```

## 7. Quiz

1. Qu'est-ce qu'un ORM ?
2. Pourquoi PostgreSQL sans mot de passe est-il acceptable ici, et seulement ici ?
3. Pourquoi convertir l'entité `Mission` en `MissionDto` avant de la renvoyer ?
4. Pourquoi les tests utilisent-ils SQLite ?

## 8. Pour aller plus loin

- EF Core : <https://learn.microsoft.com/ef/core>
- Npgsql pour EF Core : <https://www.npgsql.org/efcore/>
- Réponses typées des Minimal APIs : <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses>

<details>
<summary>Réponses du quiz</summary>

1. Un outil qui traduit des classes en tables et des requêtes C# en SQL.
2. Parce que le port n'est ouvert que sur la machine locale, en développement. En production, ce serait une faille.
3. Pour que la structure de la base puisse évoluer sans casser les clients, et pour ne jamais exposer de champ interne par accident.
4. Pour que les tests tournent partout sans Docker, et pour vérifier que le code fonctionne aussi sur SQLite.

</details>
