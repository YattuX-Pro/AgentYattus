# Leçon 64 — Éditer un agent : un éditeur, une différence, un historique

> **Commit** : `feat(admin): modifier un agent`
> **Phase** : compléter l'admin
> **Fichiers** : admin (`core/api.ts`, `pages/agents/` : `agents.ts`, `agents.html`, `diff.ts`, `diff.spec.ts`), API (`Modules/Agents/AgentsModule.cs`, `tests/…/AgentDefinitionTests.cs`)

## 1. Ce qu'on construit

Un bouton **Modifier** sur chaque fiche d'agent ouvre un panneau :

- la **consigne** dans une zone de texte, avec son compteur (20 à 8 000 caractères) ;
- ce que le code **ajoute toujours**, en lecture seule ;
- la **limite de tours** propre au rôle (vide : celle de l'instance) ;
- les **différences avec l'origine**, ligne par ligne ;
- l'**historique**, avec « Restaurer » sur chaque version, et « Revenir à l'origine ».

## 2. Montrer tout ce que reçoit le modèle

Le texte éditable n'est pas tout ce que lit le modèle : le code y ajoute les garde-fous (leçon 63). L'éditeur affiche ces garde-fous **sous** la zone de texte, en lecture seule. Ainsi l'administrateur voit la consigne complète et sait ce qu'il ne peut pas retirer.

## 3. Une différence ligne à ligne

```ts
lcs[i][j] = a[i] === b[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
```

C'est l'algorithme de la **plus longue sous-suite commune** (*LCS*), celui qu'utilise `diff`. Le tableau `lcs[i][j]` donne la longueur commune maximale entre la fin des deux textes. On le parcourt ensuite pour dire, ligne par ligne, ce qui est gardé, retiré ou ajouté. Il coûte O(n × m), sans importance pour une consigne de quelques dizaines de lignes : le commentaire `ponytail:` le note.

## 4. Restaurer, c'est encore ajouter

« Restaurer la version 2 » n'efface pas les versions 3 et 4 : cela crée une **version 5**, identique à la 2. L'historique dit toujours la vérité : on voit qu'on est revenu en arrière, quand, et qui l'a fait.

## 5. Correction de sécurité : qui voit les consignes

La revue de sécurité automatique a relevé une incohérence : l'historique des consignes était réservé aux administrateurs, mais `GET /agents`, ouvert à tous, renvoyait la consigne en vigueur. Une consigne peut contenir des règles internes. `GET /agents` ne renvoie donc les consignes, les garde-fous et la version **qu'aux administrateurs de l'instance**. Les autres voient le rôle, son modèle et ses outils. Un test le vérifie.

## 6. Quiz

1. Pourquoi afficher les garde-fous dans l'éditeur, alors qu'on ne peut pas les modifier ?
2. Que fait « Restaurer la version 2 » quand on est à la version 4 ?
3. Pourquoi les consignes ne sont-elles plus envoyées à tout le monde ?

<details>
<summary>Réponses du quiz</summary>

1. Pour que l'administrateur voie tout ce que reçoit le modèle, et sache ce qu'il ne peut pas retirer.
2. Elle crée une version 5, identique à la 2 ; l'historique garde la trace du retour en arrière.
3. Elles peuvent contenir des règles internes, et l'historique était déjà réservé aux administrateurs : il fallait être cohérent.

</details>
