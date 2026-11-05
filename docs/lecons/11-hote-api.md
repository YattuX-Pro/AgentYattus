# Leçon 11 — L'hôte de l'API, `/health` et les contrats

> **Commit** : `feat(api): hôte, /health et contrats`
> **Phase** : 2, backend
> **Fichiers** : `src/AgentYattus.Api/` (`Program.cs`, `.csproj`, `appsettings.json`, `launchSettings.json`), `src/AgentYattus.Contracts/`, `tests/AgentYattus.Api.Tests/` (`ApiFactory.cs`, `HealthTests.cs`)

## 1. Ce qu'on construit

Le **squelette du serveur** : une application web qui démarre, répond « je vais bien » sur `/health`, et un petit projet `Contracts` qui décrit les données échangées avec les clients.

> **Une image** : on ouvre la boutique. Pas encore de marchandise, mais la porte s'ouvre, la lumière s'allume, et la pancarte dit « ouvert ».

## 2. Le problème

Avant d'écrire la moindre fonctionnalité, il faut :

- un programme qui **écoute** des requêtes HTTP ;
- un moyen de savoir **s'il est vivant** (pour toi, pour Docker, pour un hébergeur) ;
- un **langage commun** avec le desktop et le téléphone : les mêmes noms de champs, les mêmes types.

## 3. Le code expliqué

### 3.1 `Program.cs` : 5 lignes pour un serveur

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

1. `CreateBuilder` prépare l'application : configuration, journaux, serveur web (Kestrel).
2. `AddHealthChecks` **enregistre** le service de vérification de santé.
3. `Build` assemble l'application.
4. `MapHealthChecks("/health")` **branche** ce service sur l'adresse `/health`.
5. `Run` démarre l'écoute, jusqu'à l'arrêt du programme.

Ce découpage revient partout en ASP.NET Core : **on enregistre des services** (`builder.Services.Add…`), **puis on branche des adresses** (`app.Map…`).

### 3.2 `launchSettings.json` : où écouter en développement

```json
"applicationUrl": "http://localhost:5080"
```

En développement, l'API écoute sur le port 5080 de ta machine, et seulement là.

### 3.3 `appsettings.json` : une règle de sécurité dès le départ

```json
"AllowedHosts": "localhost;127.0.0.1;[::1]"
```

L'API refuse toute requête qui ne s'adresse pas à la machine locale. Tant qu'il n'y a pas d'authentification, elle ne doit pas être joignable depuis le réseau (voir l'ADR 0004).

### 3.4 `Contracts` : le langage commun

```csharp
[JsonConverter(typeof(JsonStringEnumConverter<MissionStatus>))]
public enum MissionStatus { Pending, Running, Completed, Failed }

public sealed record CreateMissionRequest(string Goal);
public sealed record MissionDto(Guid Id, string Goal, MissionStatus Status, DateTimeOffset CreatedAt);
```

- Un **DTO** (*Data Transfer Object*) est un objet qui sert uniquement à **transporter** des données sur le réseau, sans aucune logique.
- `record` : une classe dont l'égalité se fait sur les valeurs, idéale pour des données immuables.
- `JsonStringEnumConverter` : le statut part en JSON sous forme de texte (`"Running"`) plutôt que de nombre (`1`). C'est plus lisible, et ajouter un statut ne décalera pas les numéros.
- Ce projet ne dépend de rien : ni de l'API, ni d'une interface. L'API **et** les clients peuvent donc le référencer.

## 4. Tester une API sans la lancer

```csharp
public sealed class ApiFactory : WebApplicationFactory<Program>;
```

`WebApplicationFactory` démarre l'API **en mémoire**, dans le processus du test : pas de port réseau, pas de serveur à lancer à côté. On lui demande un `HttpClient`, et on envoie de vraies requêtes HTTP.

```csharp
var response = await factory.CreateClient().GetAsync("/health", ...);
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

C'est un **test d'intégration** : il traverse toute la chaîne (routage, services, réponse HTTP), pas seulement une méthode isolée.

## 5. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

Ou lance l'API, puis ouvre <http://localhost:5080/health> dans un navigateur : la page affiche `Healthy`.

```bash
dotnet run --project src/AgentYattus.Api --launch-profile http
```

## 6. Quiz

1. Quelles sont les deux grandes étapes de `Program.cs` ?
2. À quoi sert un DTO ?
3. Pourquoi le projet `Contracts` ne dépend-il de rien ?
4. Qu'apporte `WebApplicationFactory` ?

## 7. Pour aller plus loin

- Minimal APIs : <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/overview>
- Health checks : <https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks>
- Tests d'intégration : <https://learn.microsoft.com/aspnet/core/test/integration-tests>

<details>
<summary>Réponses du quiz</summary>

1. Enregistrer les services (`builder.Services…`), puis brancher les adresses (`app.Map…`).
2. À transporter des données entre l'API et les clients, sans logique.
3. Pour que l'API et tous les clients puissent le référencer sans entraîner de dépendances inutiles.
4. Elle démarre l'API en mémoire pour les tests, sans port réseau, et fournit un `HttpClient` qui lui parle directement.

</details>
