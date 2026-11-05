# Leçon 14 — Le journal d'audit

> **Commit** : `feat(audit): journal des événements`
> **Phase** : 2, backend
> **Fichiers** : `Modules/Audit/AuditModule.cs`, `Modules/Missions/MissionWorker.cs`, `Program.cs`, `tests/AgentYattus.Api.Tests/ApiFactory.cs`, `MissionsTests.cs`

## 1. Ce qu'on construit

Un **journal** qui note chaque événement d'une mission, avec son heure : « passée à Running à 10:42:03 », « passée à Completed à 10:42:15 ».

> **Une image** : la boîte noire d'un avion. On ne la regarde presque jamais, mais le jour où quelque chose tourne mal, c'est elle qui dit ce qui s'est passé, et dans quel ordre.

## 2. Le problème

Le statut d'une mission ne garde que **le dernier état**. Quand des agents agiront pour toi (écrire un fichier, plus tard envoyer un message), il faudra pouvoir répondre à : **qui a fait quoi, quand, et pourquoi ?** C'est la **traçabilité**, l'une des promesses d'Agent Yattus.

## 3. Le code expliqué

### 3.1 L'événement

```csharp
internal sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid MissionId { get; set; }
    public required string Type { get; set; }
    public DateTimeOffset At { get; set; }
}
```

Un journal d'audit **n'efface et ne modifie jamais rien** : on ajoute des lignes, c'est tout. On parle de journal *append-only*.

### 3.2 Un module à part, avec sa table à lui

```csharp
modelBuilder.HasDefaultSchema("audit");
modelBuilder.Entity<AuditEvent>().HasIndex(e => e.MissionId);
```

- Schéma `audit` : le module a son propre `DbContext` et ses propres tables, séparés de ceux des missions.
- `HasIndex(e => e.MissionId)` : un **index** pour retrouver vite tous les événements d'une mission. Sans lui, la base parcourrait toute la table à chaque recherche.

### 3.3 Ce que le module montre aux autres

```csharp
public sealed class AuditLog
{
    internal AuditLog(AuditDb db) => _db = db;
    public async Task AppendAsync(Guid missionId, string type, CancellationToken cancellationToken) { ... }
}
```

- `AuditLog` est **public** : c'est la seule porte d'entrée du module.
- `AuditDb` et `AuditEvent` sont **internal** : aucun autre module ne peut écrire directement dans la table.
- Le constructeur est `internal` : seul le module Audit peut créer un `AuditLog`. Les autres le **reçoivent** par injection de dépendances.

### 3.4 Le module Missions s'en sert

```csharp
internal sealed class MissionRunner(MissionsDb db, AuditLog audit, IAgentRunner agent, ...)
{
    async Task SetStatusAsync(MissionStatus newStatus)
    {
        mission.Status = newStatus;
        await db.SaveChangesAsync(cancellationToken);
        await audit.AppendAsync(missionId, newStatus.ToString(), cancellationToken);
    }
}
```

À chaque changement de statut : on enregistre la mission, **puis** on note l'événement.

> **Limite connue** : ces deux écritures ne sont pas dans une même transaction. Si l'application s'arrêtait pile entre les deux, le statut serait enregistré sans sa ligne d'audit. C'est rare, et acceptable pour un journal à ce stade du projet.

## 4. Le test

```csharp
var audit = await scope.ServiceProvider.GetRequiredService<AuditDb>().Events
    .Where(e => e.MissionId == mission.Id).Select(e => e.Type).ToListAsync(Ct);
Assert.Equal(["Running", "Completed"], audit);
```

Une mission réussie laisse exactement deux traces, **dans l'ordre**.

## 5. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

Avec PostgreSQL lancé, après une mission :

```bash
docker compose exec postgres psql -U agentyattus -c 'select * from audit."Events";'
```

## 6. Quiz

1. Que veut dire *append-only* ?
2. Pourquoi `AuditDb` est-il `internal` ?
3. À quoi sert l'index sur `MissionId` ?
4. Quelle limite connue a l'écriture du statut puis de l'audit ?

## 7. Pour aller plus loin

- Index avec EF Core : <https://learn.microsoft.com/ef/core/modeling/indexes>
- L'ADR du backend : `docs/adr/0003-architecture-backend.md`

<details>
<summary>Réponses du quiz</summary>

1. Qu'on ne fait qu'ajouter des lignes, sans jamais modifier ni effacer les anciennes.
2. Pour qu'aucun autre module ne puisse écrire directement dans la table : ils doivent passer par `AuditLog`.
3. À retrouver rapidement tous les événements d'une mission, sans parcourir toute la table.
4. Les deux écritures ne sont pas dans une même transaction : un arrêt entre les deux laisserait un statut sans sa ligne d'audit.

</details>
