# Leçon 59 — Relancer une mission sans réécrire l'histoire

> **Commit** : `feat(missions): relancer une mission`
> **Phase** : compléter l'admin
> **Fichiers** : `Modules/Missions/` (`MissionsModule.cs`, `Mission.cs`, migration), `Contracts/Missions.cs`, tests (`MissionControlTests.cs`)

## 1. Ce qu'on construit

`POST /missions/{id}/retry`, pour un éditeur, sur une mission **échouée ou arrêtée**. Elle crée une **nouvelle** mission (même but, même type, même projet), liée à l'originale par `RetryOf`.

## 2. Une nouvelle mission, pas une remise à zéro

On aurait pu remettre l'originale à « En file ». On ne le fait pas :

- son **journal** (outils appelés, bascules, échec) disparaîtrait dans celui de la relance ;
- son **coût** se mélangerait à celui de la relance ;
- l'**audit** doit dire ce qui s'est passé, pas ce qu'on aurait voulu.

Comme une transaction bancaire annulée, on n'efface pas la ligne : on en ajoute une nouvelle, qui pointe vers la première. `RetryOf` permet à l'écran d'afficher « relance de 01a1b1… ».

## 3. Qui relance, et quoi

- L'**auteur** de la relance est celui qui clique, pas l'auteur d'origine : c'est lui qui en prend la responsabilité.
- Une mission **réussie** ne se relance pas (409) : on lance une nouvelle mission si on veut une autre réponse.
- Une mission **antérieure aux projets** non plus : elle n'a pas de projet où être relancée.

## 4. Quiz

1. Pourquoi créer une nouvelle mission plutôt que remettre l'originale en file ?
2. Qui est l'auteur d'une relance ?

<details>
<summary>Réponses du quiz</summary>

1. Pour garder intacts le journal et le coût de l'originale : l'audit ne se réécrit pas.
2. La personne qui a cliqué sur Relancer.

</details>
