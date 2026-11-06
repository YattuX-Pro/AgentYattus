# Leçon 70 — Bilan : l'admin complet, et ce que les revues ont appris

> **Commit** : `docs: README, sécurité, feuille de route et cours (leçons 51 à 70)`
> **Phase** : compléter l'admin
> **Fichiers** : `README.md`, `docs/securite.md`, `docs/roadmap.md`, `docs/cours/`

## 1. Ce que l'admin sait faire maintenant

| Page | Ce qu'on y fait |
|---|---|
| Vue d'ensemble | Missions, réussite, décisions, **coût du mois** et budget, tokens, bascules ; tout en direct |
| Projets | Visibilité, invités, dossier de travail |
| Missions | Suivre en direct, approuver, **arrêter**, **relancer** ; tokens et coût par mission |
| Audit | Le journal, filtrable, **exportable en CSV** |
| Agents | Modèle, outils, **statistiques**, **consignes versionnées** |
| Modèles | Fournisseurs, défaut, chaîne gratuite et son **activité du jour** |
| Organisations, Utilisateurs | Les équipes et leurs rôles |
| **Réglages** | Tours, durée, missions simultanées, fichiers exclus, rétention, **budget** |

Le bureau, lui, ne gère plus rien : il lance (dans le projet choisi), suit et approuve. La séparation de l'ADR 0008 est terminée.

## 2. Ce que les revues de sécurité ont trouvé

Pendant cette phase, la revue automatique a relevé quatre vrais problèmes, tous corrigés :

1. **Les fichiers exclus** se contournaient par un autre nom du même fichier (casse, Unicode, `::$DATA`, `~1`). Il a fallu deux passages pour arriver à la règle juste : le **nom exact du disque**.
2. **La première correction** relisait le dossier pour chaque entrée : un coût quadratique.
3. **Les consignes des agents** étaient lisibles par tout compte connecté, alors que leur historique était réservé aux administrateurs.
4. (Phase précédente, rappel) le dossier commun du serveur était lisible par n'importe quel projet.

La leçon générale : une **règle de sécurité** se vérifie sur ce que le système **résout** (le fichier réel, le droit réel), pas sur ce que l'on **demande**.

## 3. Ce que les tests ont trouvé

- Un test du composant Angular a cessé de compiler au commit 64 (un type `Agent` enrichi), et il n'a été corrigé qu'au commit 65. La CI l'aurait signalé : c'est sa raison d'être. Lancer **toutes** les suites avant chaque commit, pas seulement celle qu'on vient de toucher.
- Les tests qui figent une liste exacte (le journal d'une mission) ont signalé chaque nouvel événement : c'est voulu.

## 4. Ce qui reste, écrit

- Les **rôles d'agents personnalisés** (choisir leurs outils) : une décision à part (ADR 0011).
- Les **clés de modèle personnelles**, le modèle et le budget **par projet** (ADR 0009).
- La colonne **MFA** des utilisateurs, qui demanderait un compte de service Keycloak.

## 5. Quiz

1. Pourquoi la séparation de l'ADR 0008 est-elle « terminée » avec cette phase ?
2. Quelle leçon générale tirer des failles trouvées par les revues ?

<details>
<summary>Réponses du quiz</summary>

1. Le bureau n'a plus d'écran de gestion : il sert à travailler, et toute la gestion est dans l'admin web.
2. Vérifier une règle de sécurité sur ce que le système résout réellement, pas sur la forme de la demande.

</details>
