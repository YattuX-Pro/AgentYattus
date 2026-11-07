# Leçon 109 — L'API en mode local : SQLite et le jeton du lancement

> **Commit** : `feat(auth): mode local sans Keycloak, SQLite dans le dossier de données`
> **Phase** : A
> **Fichiers** : `Api/LocalMode.cs`, `Api/Authentication.cs`, `Api/Database.cs`, `Api/Modules/Llm/LlmModule.cs`, `Program.cs`, `tests/LocalModeTests.cs`

## 1. Ce qu'on construit

`Mode=Local` dans la configuration change trois choses, et rien d'autre :

| | Mode serveur | Mode local |
|---|---|---|
| Base | PostgreSQL, migrations | SQLite `agent-yattus.db` dans le dossier de données |
| Qui entre | Un jeton Keycloak | Le jeton du lancement (`Local:Token`) |
| Trousseau Data Protection | `DataProtection:KeysPath` | `keys/` dans le dossier de données |

Les modules ne savent pas dans quel mode ils tournent.

## 2. Le code expliqué

**Le gestionnaire d'authentification** compare le jeton **en temps constant** :

```csharp
CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(Options.Token))
```

Une comparaison ordinaire s'arrête au premier caractère différent. En mesurant la durée des réponses, on pourrait alors deviner le jeton caractère par caractère.

**La personne de la machine** reçoit `sub = local` et le rôle `llm-admin`. Elle est seule, et administratrice de son instance. Tout le reste (projets, droits, journal) fonctionne sans changement.

**Sans jeton configuré, l'API refuse de démarrer.** Elle ne tombe jamais en « mode ouvert ».

## 3. Vérifie toi-même

`dotnet test --filter LocalModeTests` : ces tests démarrent la vraie API, sans rien remplacer, sur une base SQLite dans un dossier temporaire.

- sans jeton, ou avec un mauvais jeton : 401 ;
- avec le bon : `/me` répond `local` et administrateur ;
- le fichier `agent-yattus.db` contient les tables de tous les modules.

## 4. Les limites

- **Pas de migrations SQLite.** Les tables sont créées par `EnsureCreated`. Une nouvelle colonne ne s'ajoute pas à une base locale existante. Il faudra une série de migrations SQLite avant la première mise à jour publique.
- **Un seul utilisateur.**

## 5. Quiz

1. Pourquoi l'API refuse-t-elle de démarrer sans jeton ?
2. Qu'apprendrait un attaquant d'une comparaison qui n'est pas en temps constant ?

<details>
<summary>Réponses du quiz</summary>

1. Pour qu'une erreur de lancement ne laisse jamais une API ouverte à toute la machine.
2. Combien de caractères du début sont justes, et donc le jeton, un caractère après l'autre.

</details>
