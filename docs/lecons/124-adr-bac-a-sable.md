# Leçon 124 — ADR 0015 : le bac à sable

> **Commit** : `docs(adr): 0015 bac à sable d'exécution`
> **Phase** : C
> **Fichiers** : `docs/adr/0015-bac-a-sable.md`

## 1. Le problème

Un agent développeur doit lancer `dotnet test`. Mais une commande peut tout faire : effacer un disque, lire `~/.ssh`, envoyer le code sur Internet. Et un fichier piégé du projet peut pousser l'agent à la lancer.

## 2. La décision, en une phrase

**Jamais sur la machine** : dans un conteneur Docker jetable, avec le dossier du projet seul, sans réseau ni privilège, borné en mémoire, en processus et en durée. **Et toujours avec ton accord**, commande par commande.

## 3. Pourquoi pas une liste de commandes autorisées ?

Elle rassure, mais ne protège pas. `dotnet test` **exécute le code du projet** : si ce code est piégé, la commande « autorisée » fait n'importe quoi. Ce qui protège, c'est l'endroit où elle s'exécute.

## 4. Ce qu'on accepte

- Le dossier du projet est modifiable par une commande approuvée. git permet de défaire.
- Sans réseau, pas de `npm install`. L'administrateur peut ouvrir le réseau (`Agents:SandboxNetwork = bridge`) s'il le décide.
- Un conteneur n'est pas une machine virtuelle : il partage le noyau.

## 5. Quiz

1. Pourquoi l'accord humain ne suffit-il pas, sans conteneur ?

<details>
<summary>Réponse</summary>

1. Une commande peut sembler anodine (`dotnet test`) et exécuter du code piégé. Le conteneur limite les dégâts quand l'humain ne peut pas voir le danger.

</details>
