# Leçon 134 — Les clés personnelles : chacun son compte, sans le prêter

> **Commit** : `feat(llm): les clés personnelles, pour ses propres missions`
> **Phase** : hors plan (demande du 8 novembre) · **ADR** : 0018
> **Fichiers** : `Contracts/Llm.cs`, `Api/Modules/Llm/LlmModule.cs` + migration `PersonalConnections`, `Api/Modules/Agents/{AgentsModule,AgentFrameworkRunner}.cs`, `Api/Modules/Missions/{Mission,MissionWorker,MissionsModule}.cs` + migration `PersonalCost`, `Client.Core/{ApiClient,SettingsViewModel,ShellViewModel,RecruitViewModel}.cs`, `Desktop/Views/SettingsView.axaml`, `tests/PersonalKeysTests.cs`, `tests/SettingsViewModelTests.cs`

## 1. Ce qu'on construit

Dans le bureau, le menu du portrait › **Modèles et clés** ouvre, sur un serveur, l'écran **Mes clés** :

- on y connecte sa clé (Anthropic, OpenAI, Mistral… : un fournisseur en ligne) ;
- on choisit son modèle par défaut ;
- on la révoque ;
- on revient au modèle de l'instance.

Ses missions passent alors par sa clé ; celles des autres, jamais. En mode local, le même menu ouvre toujours l'écran de l'instance.

## 2. Le problème

Une clé d'API, c'est un compte et une facture. Avant, une seule clé servait à tout le monde, connectée par un administrateur. Il faut que chacun puisse apporter la sienne **sans la prêter** : ni aux collègues d'un projet partagé, ni à l'administrateur, ni au mode Gratuit commun.

## 3. Les options

Voir l'ADR 0018. Choix : un propriétaire sur la connexion, des routes `/llm/mine` pour tous, et une règle de résolution qui part de la personne qui lance la mission.

## 4. Le code expliqué

- **`LlmConnection.OwnerId`** : null pour l'instance, sinon l'utilisateur. Un index permet de trouver vite les clés d'un utilisateur. **`LlmUserModel`** (table `UserModels`) : son modèle par défaut.
- **Les routes de l'instance** (`/llm`, `/llm/models`, révocation) ne voient que `OwnerId == null` : l'administrateur ne voit ni ne révoque une clé personnelle.
- **`/llm/mine`** :
  - `GET` : mes connexions, mon modèle, les fournisseurs à clé ;
  - `POST /connections` : refuse un serveur local ou une adresse au choix ;
  - `DELETE /connections/{id}` : 404 si ce n'est pas la mienne ;
  - `PUT /default` : une de mes connexions, ou null ;
  - `GET /models` : les modèles de mes clés.
- **`/llm/choices`** (pour « Nouveau collègue ») : l'instance et mes clés, marquées `Personal`.
- **`ValidateAsync(…, owner)`** : une connexion personnelle n'est valable que pour son propriétaire. Pour un autre, elle est traitée comme « inconnue », pour ne pas révéler qu'elle existe. Le défaut de l'instance et la chaîne Gratuite passent `owner: null` : jamais une clé personnelle.
- **`LlmClientProvider.GetAsync(role, user, …)`** :
  1. **`PersonalAsync`** d'abord : le modèle du rôle s'il passe par une clé de `user`, puis son modèle par défaut ;
  2. puis le mode Gratuit, **sur les connexions de l'instance seulement** ;
  3. puis l'instance (`ResolveAsync` exige `OwnerId: null`).
  - Le `LlmClient` rendu porte **`Personal`**.
- **Qui est `user` ?**
  - pour une mission : `mission.CreatedBy`, passé par le runner (`launchedBy`) ;
  - pour l'écran Agents : `me.Id`. Chacun voit le modèle que **ses** missions utiliseraient.
- **Le budget** :
  - `AgentUsage.PersonalCostMicros` reçoit le coût quand le client est personnel ;
  - la mission l'additionne dans `PersonalCostMicros` ;
  - `SpentThisMonthMicrosAsync` soustrait cette part ;
  - `UsesPaidModelAsync` ignore un rôle servi par une clé personnelle : le plafond de l'instance ne bloque pas une mission qu'elle ne paie pas.
- **Le journal** note `cle:personnelle` ou `cle:instance` après `modele:…`.
- **Le bureau** :
  - `SettingsViewModel(api, personal: true)` appelle seulement `/llm/mine*` et cache le mode Gratuit et les modèles par agent ;
  - `ShellViewModel.CanManageModels` est toujours vrai : l'entrée du menu existait déjà, elle ouvre l'écran qui convient au mode.

## 5. Vérifie toi-même

```bash
dotnet test tests/AgentYattus.Api.Tests --filter-class "*PersonalKeysTests"
```

```bash
dotnet test tests/AgentYattus.Client.Core.Tests --filter-class "*SettingsViewModelTests"
```

- **Cloisonnement** :
  - Alice voit sa clé, sans la clé elle-même (seulement ses 4 derniers caractères) ;
  - Bob et l'administrateur ne la voient pas ;
  - Bob ne peut ni la révoquer, ni la prendre comme modèle par défaut ;
  - l'administrateur ne peut la mettre ni comme défaut de l'instance, ni sur un agent de l'instance ;
  - un serveur local est refusé ;
  - révoquée, la clé chiffrée est effacée.
- **Résolution** :
  - la mission d'Alice prend sa clé (`Personal`), celle de Bob la clé de l'instance ;
  - le mode Gratuit ne prend jamais la clé d'Alice.
- **Budget** : une mission payée par une clé personnelle coûte 0,0002 $, note `cle:personnelle`, et le budget de l'instance reste à 0.

## 6. Les limites

- La clé est déchiffrée **sur le serveur** : quelqu'un qui a la base **et** le trousseau Data Protection peut la lire. Ce n'est pas un coffre de bout en bout.
- Pas de serveur local personnel, ni de budget par utilisateur (le coût personnel est compté, pas plafonné).
- Dans un projet partagé, une mission coûte à **qui la lance** : il n'y a pas encore de clé de projet.
- L'entrée du menu garde son libellé « Modèles et clés » : le titre de l'écran, lui, dit « Mes clés ».

## 7. Quiz

1. Pourquoi une clé personnelle d'un autre est-elle « inconnue » plutôt qu'« interdite » ?
2. Pourquoi refuser Ollama en clé personnelle, alors qu'il est gratuit ?
3. Pourquoi le mode Gratuit ne prend-il pas les modèles gratuits d'une clé personnelle ?

<details><summary>Réponses</summary>

1. « Interdite » dirait qu'elle existe : on apprendrait qu'un collègue a une clé, et son identifiant. « Inconnue » ne révèle rien.
2. Sur un serveur, l'adresse `http://localhost:11434` est celle **du serveur**, pas de son poste. Laisser chacun choisir l'adresse appelée par le serveur ouvrirait une porte vers ses services internes.
3. La chaîne Gratuite sert à **tout le monde** : y mettre une clé personnelle ferait payer (ou consommer le quota de) son propriétaire pour les missions des autres.

</details>
