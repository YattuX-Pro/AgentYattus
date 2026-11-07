# Leçon 125 — run_command, dans un conteneur jetable

> **Commit** : `feat(agents): run_command, une commande dans un conteneur isolé, avec ton accord`
> **Phase** : C
> **Fichiers** : `Api/Modules/Agents/{Sandbox,WorkspaceTools,AgentFrameworkRunner,AgentsModule}.cs`, `Team/TeamContent.cs`, `Client.Core/{MissionViewModel,OfficeViewModel,RecruitViewModel}.cs`, `tests/SandboxTests.cs`

## 1. La commande Docker

```
docker run --rm --name agent-yattus-… --label agent-yattus=sandbox
  --network none --memory 1g --cpus 2 --pids-limit 256
  --cap-drop ALL --security-opt no-new-privileges
  --mount type=bind,source=<dossier du projet>,target=/work --workdir /work
  <image> sh -c "<la commande>"
```

| Option | Ce qu'elle empêche |
|---|---|
| `--rm` | Un conteneur qui reste après la commande |
| `--network none` | Envoyer le code ou des secrets sur Internet, télécharger un outil |
| `--memory`, `--cpus`, `--pids-limit` | Saturer la machine (« fork bomb ») |
| `--cap-drop ALL`, `no-new-privileges` | Gagner des droits dans le conteneur |
| `--mount … target=/work` seul | Lire le reste du disque (`~/.ssh`…) |
| `--mount` plutôt que `-v` | Un chemin contenant « : » qui changerait le sens de l'option |

## 2. Trois détails qui comptent

- **Les arguments un par un** (`ProcessStartInfo.ArgumentList`) : aucun shell de la machine n'interprète la commande. Seul le `sh` du conteneur le fait.
- **La durée** : au-delà de 120 secondes, on tue **le conteneur** (`docker kill <nom>`), pas seulement le client `docker`, qui laisserait le conteneur tourner.
- **La fin de la sortie** : on garde les 20 000 derniers caractères, là où sont les erreurs et le résumé des tests.

## 2 bis. Après revue de sécurité (un correctif à part)

- **Les fichiers exclus sont masqués** dans le conteneur. Un `.env` exclu pour `read_file` était lisible par `cat .env`. Chaque fichier exclu est recouvert d'un fichier vide en lecture seule (`/dev/null`), chaque dossier exclu d'un dossier vide (`tmpfs`). Au-delà de 200 à masquer, la commande est refusée.
- **`.git` en lecture seule** : une commande pourrait y planter un *hook*, que git exécuterait ensuite **sur ta machine**, hors du bac à sable.
- **La sortie est lue par morceaux de taille fixe**, pas par ligne : une seule « ligne » de 60 millions de caractères ne s'accumule plus en mémoire. Au-delà de 50 millions de caractères en tout, la commande est arrêtée.

Une deuxième revue a ajouté deux règles :

- **Un chemin qui contient une virgule, un guillemet ou un « = » est refusé.** Docker lit `--mount` comme une liste « clé=valeur » séparée par des virgules. Un fichier nommé `x,readonly=false` changerait les options du montage. On refuse plutôt que d'échapper.
- **Tout le dossier est parcouru pour trouver les fichiers exclus**, `node_modules` et `.git` compris : un secret peut s'y cacher. Au-delà de 50 000 entrées, la commande est refusée.

## 3. Ce que tu vois

La demande d'accord montre la commande exacte, avec un avertissement : « Dans un conteneur isolé, sans réseau. Elle peut modifier les fichiers du projet. » L'open space raconte « Je lance « dotnet test » dans le bac à sable. ».

## 4. Vérifie toi-même

`dotnet test --filter SandboxTests` (Docker et l'image `alpine:3` nécessaires, sinon ignoré) :

- la commande écrit dans le projet et ne voit pas le disque de la machine ;
- `wget` échoue (pas de réseau) ;
- `sleep 60` est tué au bout de 3 secondes ;
- les arguments construits ne contiennent jamais la commande en dehors du dernier ;
- un `.env` exclu reste invisible et intact, `.git` ne se modifie pas, une sortie sans fin est arrêtée.

## 5. Quiz

1. Pourquoi tuer le conteneur par son nom plutôt que le processus `docker` ?

<details>
<summary>Réponse</summary>

1. Le processus `docker` n'est qu'un client : le tuer laisse le conteneur tourner dans le démon Docker, avec ses ressources.

</details>
