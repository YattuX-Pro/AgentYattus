# Leçon 108 — ADR 0014 : le mode local, et qui choisit le dossier

> **Commit** : `docs(adr): 0014 mode local et mode serveur`
> **Phase** : A, « Mon dossier, mes fichiers »
> **Fichiers** : `docs/adr/0014-mode-local-et-dossiers.md`

## 1. Le problème

Pour essayer Agent Yattus, il faut aujourd'hui Docker, PostgreSQL, Keycloak et l'API. Ensuite, seul l'administrateur choisit le dossier d'un projet. Un développeur ne peut donc pas pointer vers son dépôt, et une personne qui n'est pas développeuse ne passe pas l'installation.

## 2. Ce que l'ADR décide

- **Mode local** :
  - le logiciel lance l'API sur `127.0.0.1` ;
  - la base est un fichier SQLite dans le dossier de données de l'utilisateur ;
  - il n'y a pas de Keycloak ;
  - un **jeton aléatoire par lancement** sert de porte d'entrée.
- **Mode serveur** : le propriétaire d'un projet choisit son dossier, mais seulement sous les dossiers autorisés par l'administrateur.

## 3. Pourquoi un jeton, même sur sa propre machine ?

Une API sans authentification sur `localhost` est joignable par **n'importe quel programme**, et même par une page web ouverte dans le navigateur, qui peut envoyer des requêtes vers localhost. Elle pourrait lancer des missions et lire tes fichiers. Le jeton, tiré au hasard à chaque lancement, ferme cette porte.

Il passe par une **variable d'environnement**, et non par la ligne de commande : `ps` affiche les arguments des processus à tous les utilisateurs de la machine.

## 4. Quiz

1. Pourquoi ne pas simplement « écouter seulement sur 127.0.0.1 » sans jeton ?
2. Pourquoi l'administrateur garde-t-il la liste des dossiers autorisés en mode serveur ?

<details>
<summary>Réponses du quiz</summary>

1. Tous les programmes de la machine, et les pages web, peuvent joindre 127.0.0.1.
2. Sinon, un utilisateur ferait lire au serveur n'importe quel dossier : `/etc`, ou ceux des autres.

</details>
