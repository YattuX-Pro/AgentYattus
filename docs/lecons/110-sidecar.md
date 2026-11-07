# Leçon 110 — Le logiciel lance sa propre API (sidecar)

> **Commit** : `feat(desktop): l'API démarre avec le logiciel (sidecar, sans Docker)`
> **Phase** : A
> **Fichiers** : `Client.Core/LocalBackend.cs`, `Desktop/App.axaml.cs`, `Client.Core/ShellViewModel.cs`, `MainWindow.axaml`, `Api/LocalMode.cs` (ParentWatcher), `tests/.../LocalBackendTests.cs`

## 1. Ce qu'on construit

Au lancement, si le mode local est choisi, le logiciel :

1. choisit un **port libre** sur 127.0.0.1 et tire un **jeton** de 256 bits ;
2. lance l'API (`AgentYattus.Api`) avec `Mode=Local`, le jeton et le port, **par l'environnement** ;
3. affiche la fenêtre tout de suite, attend que `/health` réponde, puis se connecte au hub et charge l'accueil ;
4. arrête l'API à la fermeture (`Kill(entireProcessTree: true)`).

Il n'y a ni écran de connexion, ni « Se déconnecter », ni lien vers l'espace admin web : la personne de la machine est seule.

## 2. Quel mode ?

| Situation | Mode |
|---|---|
| `AGENTYATTUS_MODE=server` ou `AGENTYATTUS_API_URL` défini | Serveur (Keycloak), comme avant |
| `AGENTYATTUS_MODE=local` | Local : l'API installée à côté du logiciel, sinon la compilation du dépôt |
| Rien de défini, et `api/` à côté du logiciel | Local : c'est la version installée |
| Rien de défini, pas d'`api/` | Serveur : le développement d'aujourd'hui ne change pas |

En développement :

```bash
dotnet build src/AgentYattus.Api && AGENTYATTUS_MODE=local dotnet run --project src/AgentYattus.Desktop
```

## 3. Et si le logiciel plante ?

Un processus enfant ne meurt pas avec son parent. Une API orpheline continuerait de tourner, avec son port ouvert. Le logiciel transmet donc son numéro de processus (`Local__ParentPid`). Dans l'API, `ParentWatcher` vérifie toutes les deux secondes qu'il existe encore, et arrête l'API sinon.

## 3 bis. Et si un autre programme prend le port ? (correctif après revue)

Entre le moment où le logiciel choisit le port et celui où l'API l'occupe, un autre programme pourrait le prendre, et recevoir le jeton à la première requête. Une revue de sécurité automatique l'a relevé. Le correctif :

- **L'API prouve qu'elle connaît le jeton, sans l'envoyer.** Le logiciel tire un nonce, l'API répond HMAC-SHA256(jeton, nonce) sur `/health/local`, et le logiciel compare en temps constant.
- **Le jeton n'est attaché aux requêtes qu'après cette preuve.** Avant, aucune requête ne le porte.
- **Si la preuve est fausse, le démarrage est annulé.**

De même, la recherche de `deploy/portraits` en remontant les dossiers ne se fait qu'en développement. Une version installée ne prend que `portraits/` à côté d'elle.

## 4. Vérifie toi-même

`dotnet test --filter LocalBackendTests` lance **la vraie API**, avec une base dans un dossier temporaire :

- sans jeton, 401 ; avec le jeton, 200 ;
- deux lancements ont deux jetons, et le jeton n'apparaît jamais dans les arguments de la commande ;
- un intrus qui occupe le port fait échouer le démarrage et ne reçoit jamais d'en-tête `Authorization`.

## 5. Les limites

- Le logiciel ne compile pas l'API pour toi en développement. L'installeur (phase F) la mettra dans `api/`.
- Il faut le runtime .NET si l'API est livrée en `.dll`. L'installeur la publiera en exécutable autonome.
- Pas encore d'écran pour connecter un modèle sans l'espace admin : c'est le prochain commit.

## 6. Quiz

1. Pourquoi choisir le port **avant** de lancer l'API ?
2. Que se passerait-il sans `ParentWatcher` après un plantage du logiciel ?

<details>
<summary>Réponses du quiz</summary>

1. Pour créer les clients HTTP et SignalR tout de suite, avec la bonne adresse, et afficher la fenêtre sans attendre.
2. L'API resterait lancée, port ouvert, jusqu'au redémarrage de la machine ; et le prochain lancement en démarrerait une deuxième.

</details>
