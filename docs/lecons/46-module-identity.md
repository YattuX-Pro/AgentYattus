# Leçon 46 — Le module Identity : un annuaire, des organisations, des rôles

> **Commit** : `feat(identity): annuaire des utilisateurs, organisations et rôles`
> **Phase** : multi-utilisateur
> **Fichiers** : `src/AgentYattus.Api/Modules/Identity/` (`IdentityModule.cs`, migration), `Contracts/Identity.cs`, `Program.cs`, tests (`IdentityTests.cs`, `ApiFactory.cs`), `evals/…/ModelEval.cs`

## 1. Ce qu'on construit

| Route | Qui | Effet |
|---|---|---|
| `GET /me` | tout le monde | Qui je suis, mes organisations et mes rôles |
| `GET /users` | admin de l'instance | L'annuaire complet |
| `GET /users/lookup?q=` | tout le monde | Chercher quelqu'un pour partager (10 résultats au plus) |
| `GET /organizations` | tout le monde | Mes organisations (toutes pour un admin de l'instance) |
| `POST /organizations` | admin de l'instance | Créer une organisation ; on en devient administrateur |
| `PUT /organizations/{id}` | admin de l'organisation | Nom, visibilité par défaut des nouveaux projets |
| `PUT` et `DELETE /organizations/{id}/members/{userId}` | admin de l'organisation | Ajouter, changer de rôle, retirer |

## 2. L'annuaire se remplit tout seul

```csharp
app.UseAuthentication();
app.UseUserDirectory();   // inscrit chaque utilisateur authentifié
app.UseAuthorization();
```

Un **middleware** est un maillon de la chaîne qui traite chaque requête. Celui-ci, placé juste après l'authentification, enregistre la personne : son `sub` (l'identifiant **stable** de Keycloak, qui ne change pas si le nom change), son nom et sa dernière visite.

On appelle cela du *provisionnement à la volée* (*just-in-time provisioning*). Pas de synchronisation avec Keycloak : on connaît ceux qui sont venus.

Pour ne pas écrire en base à chaque requête, `UserDirectoryThrottle` retient la dernière écriture : **une toutes les 5 minutes** par personne, sauf si son nom ou son rôle change.

⚠️ Deux premières requêtes simultanées pourraient insérer la même personne deux fois. La clé primaire l'interdit, et on ignore l'erreur dans ce seul cas :

```csharp
catch (DbUpdateException) when (user is null) { db.ChangeTracker.Clear(); }
```

## 3. Qui suis-je ? `ICurrentUser`

```csharp
public interface ICurrentUser { string Id { get; } string Username { get; } bool IsInstanceAdmin { get; } }
```

Les autres modules ne lisent jamais les revendications du jeton eux-mêmes : ils demandent `ICurrentUser`. Si demain l'identité vient d'ailleurs, un seul fichier change.

## 4. Une organisation n'est jamais sans administrateur

```csharp
if (membership.Role == OrganizationRole.Admin && await IsLastAdminAsync(db, id, ct))
    return Invalid("userId", "C'est le dernier administrateur : nommez-en un autre d'abord.");
```

C'est un **invariant** : une règle toujours vraie. Le créateur devient administrateur, et le dernier administrateur ne peut ni partir ni se rétrograder. Sinon l'organisation deviendrait ingérable.

## 5. 403 pour un membre, 404 pour un inconnu

Un opérateur qui tente de gérer les membres reçoit **403** : il sait que l'organisation existe. Quelqu'un d'extérieur reçoit **404** : on ne lui révèle rien (ADR 0009).

## 6. Tester avec plusieurs personnes

```csharp
var alice = As(api, "alice");   // en-tête X-Test-User
var bob = As(api, "bob");
Assert.Empty(await bob.GetFromJsonAsync<OrganizationDto[]>("/organizations"));
```

Le faux schéma d'authentification des tests accepte maintenant le nom de l'utilisateur. Chaque règle se teste avec **deux personnes** : celle qui a le droit et celle qui ne l'a pas.

## 7. Quiz

1. Pourquoi identifier quelqu'un par `sub` plutôt que par son nom d'utilisateur ?
2. Pourquoi l'annuaire n'écrit-il pas à chaque requête ?
3. Quel invariant protège une organisation ?
4. Pourquoi `GET /users/lookup` est-il limité à 10 résultats et 2 caractères ?

<details>
<summary>Réponses du quiz</summary>

1. Le nom peut changer ; `sub` est stable : les droits ne se perdent pas.
2. Pour ne pas faire une écriture en base par requête : une toutes les 5 minutes suffit.
3. Il reste toujours au moins un administrateur.
4. Pour qu'on ne puisse pas télécharger tout l'annuaire d'un coup : on cherche quelqu'un, on ne liste pas tout le monde.

</details>
