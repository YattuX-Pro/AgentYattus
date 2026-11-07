# ADR 0015 — Le bac à sable d'exécution

- **Statut** : accepté
- **Date** : 2026-11-07

## Contexte

Un agent développeur doit lancer les tests, compiler, exécuter un script. C'est l'outil le plus puissant, et le plus dangereux : une commande peut effacer un disque, voler des clés (`~/.ssh`, `~/.aws`), ou envoyer le code sur Internet. Le texte d'un fichier lu peut aussi pousser l'agent à lancer une telle commande (injection de prompt, leçon 03).

## Décision

1. **Un outil `run_command(command)`, avec l'accord humain à chaque appel**, comme une écriture. L'humain voit la commande exacte avant qu'elle parte.
2. **Jamais sur la machine elle-même : dans un conteneur Docker jetable**, créé pour la commande puis supprimé (`--rm`) :
   - **seul le dossier du projet est monté**, dans `/work` : ni le dossier personnel, ni les clés, ni le reste du disque ;
   - **pas de réseau** par défaut (`--network none`) : rien ne sort, rien n'est téléchargé ;
   - **des ressources bornées** : 1 Go de mémoire, 2 processeurs, 256 processus (une « fork bomb » s'arrête là) ;
   - **aucun privilège** : `--cap-drop ALL`, `--security-opt no-new-privileges` ; sous Linux, l'utilisateur de l'API plutôt que root ;
   - **une durée bornée** : 120 secondes par défaut. Au-delà, le conteneur est tué (`docker kill`).
3. **La commande ne passe jamais par un shell de la machine.** Docker reçoit ses arguments un par un (`ArgumentList`). Seul le shell **du conteneur** interprète la commande.
4. **La sortie est une donnée** : coupée aux 20 derniers Ko, rendue au modèle avec le code de sortie.
5. **Réglages de l'instance** (`Agents:Sandbox*`) : l'image (par défaut `mcr.microsoft.com/dotnet/sdk:10.0`), la durée, et le réseau (`none`, ou `bridge` si l'administrateur l'accepte pour `dotnet restore`, `npm install`).
6. **Sans Docker**, l'outil répond « bac à sable indisponible ». Il n'y a jamais de repli sur la machine.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Lancer sur la machine, avec l'accord humain | Simple | Une seule erreur de lecture de l'humain, et tout le disque est exposé |
| Une liste de commandes autorisées | Rassurant | `dotnet test` exécute le code du projet, donc n'importe quoi : la liste ne protège pas |
| Une machine virtuelle par commande | Isolation plus forte | Lent, lourd à installer |
| **Conteneur jetable + accord** | Isolation forte, quelques secondes au démarrage | Docker requis ; le dossier du projet reste modifiable |

## Conséquences

- (+) Le pire qu'une commande puisse faire : abîmer le dossier du projet, ce que git permet de défaire.
- (−) **Le dossier du projet est monté en écriture** : une compilation écrit `bin/` et `obj/`. Une commande approuvée peut donc modifier les fichiers du projet.
- (−) Sans réseau, `dotnet restore` ou `npm install` échouent. Il faut un projet déjà restauré, ou le réseau ouvert par l'administrateur.
- (−) Un conteneur partage le noyau de la machine : ce n'est pas une frontière aussi forte qu'une machine virtuelle.
