# Leçon 18 — Lister les missions : `GET /missions`

> **Commit** : `feat(missions): GET /missions`
> **Phase** : 2, backend
> **Fichiers** : `src/AgentYattus.Api/Modules/Missions/MissionsModule.cs`, `tests/AgentYattus.Api.Tests/MissionsTests.cs`

## 1. Ce qu'on construit

Une route qui renvoie **la liste des missions**, des plus récentes aux plus anciennes :

```
GET /missions?limit=20
```

Jusqu'ici, le desktop n'affichait que les missions lancées **depuis sa propre fenêtre**. Ouvrir l'application sur une autre machine, ou la relancer, faisait « disparaître » les missions. Avec cette route, chaque client peut afficher **toutes** les missions.

## 2. Le problème : une liste qui grossit

Après un an d'utilisation, il peut y avoir des milliers de missions. Tout renvoyer d'un coup serait lent pour la base, lourd sur le réseau, et inutile pour l'écran, qui n'en affiche qu'une vingtaine.

On **pagine** donc : le client demande au plus *N* missions.

## 3. Le code expliqué

```csharp
private static async Task<Results<Ok<MissionDto[]>, ValidationProblem>> ListAsync(
    MissionsDb db, CancellationToken cancellationToken, int limit = DefaultPageSize)
```

- `int limit = DefaultPageSize` : ASP.NET Core lit `?limit=` dans l'adresse. S'il est absent, on prend 50.
- Le type de retour annonce les deux réponses possibles : la liste (`200`), ou une erreur de validation (`400`).

```csharp
if (limit is < 1 or > MaxPageSize)
    return TypedResults.ValidationProblem(...);
```

On **valide** la limite. Sans maximum, un client pourrait demander `?limit=1000000` et faire travailler la base pour rien. C'est une petite protection contre les abus.

```csharp
var page = await db.Missions.OrderByDescending(m => m.Id).Take(limit).ToListAsync(cancellationToken);
```

- `OrderByDescending(m => m.Id)` : les plus récentes d'abord.
- `Take(limit)` : au plus *limit* lignes. EF Core traduit tout ça en **une seule requête SQL** (`ORDER BY … LIMIT …`) : le tri et la coupe se font dans la base, pas en mémoire.

### Pourquoi trier par `Id` et pas par date ?

Les identifiants sont des **GUID version 7** (leçon 12) : leurs premiers caractères **sont** l'heure de création, à la milliseconde. Trier par `Id`, c'est donc trier par date. Et c'est utile ici : **SQLite**, utilisé dans les tests et prévu pour le mode local (ADR 0004), ne sait pas trier sur un `DateTimeOffset`.

Une nuance : deux missions créées dans la **même milliseconde** peuvent sortir dans n'importe quel ordre entre elles. C'est sans importance pour un affichage.

## 4. La limite acceptée

```csharp
// ponytail: une seule page, les plus récentes d'abord ; ajouter un curseur (?before=) quand 100 ne suffira plus.
```

On ne peut pas encore demander **la page suivante**. La bonne technique, quand il le faudra, sera un **curseur** : « donne-moi les 50 missions plus anciennes que celle-ci ». C'est plus fiable que `?page=3`, qui saute ou répète des éléments si des missions sont créées pendant qu'on fait défiler.

## 5. Les tests

| Test | Vérifie |
|---|---|
| `List_returns_the_most_recent_missions_first` | Sur 3 missions, `?limit=2` renvoie les 2 dernières, la plus récente en premier |
| `List_refuses_a_limit_out_of_range` | `?limit=0` et `?limit=101` sont refusés avec un 400 |

Le premier test crée sa **propre** API (`new ApiFactory()`), avec une base vide. Il ne voit donc pas les missions des autres tests.

## 6. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

Avec l'API lancée :

```bash
curl "http://localhost:5080/missions?limit=5"
```

## 7. Quiz

1. Pourquoi paginer dès le début ?
2. Pourquoi fixer une limite maximale ?
3. Pourquoi peut-on trier par `Id` pour avoir l'ordre chronologique ?
4. Quel est le défaut d'une pagination par numéro de page (`?page=3`) ?

## 8. Pour aller plus loin

- Pagination avec EF Core : <https://learn.microsoft.com/ef/core/querying/pagination>
- Liaison des paramètres (Minimal APIs) : <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding>

<details>
<summary>Réponses du quiz</summary>

1. Parce que la liste grossit sans fin : tout renvoyer deviendrait lent, lourd et inutile.
2. Pour qu'un client ne puisse pas faire travailler la base inutilement en demandant une liste énorme.
3. Parce qu'un GUID version 7 commence par l'heure de sa création.
4. Si des éléments sont ajoutés pendant qu'on fait défiler, certains sont sautés ou répétés. Un curseur évite ce problème.

</details>
