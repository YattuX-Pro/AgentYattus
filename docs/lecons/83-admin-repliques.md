# Leçon 83 — Traits et répliques : un éditeur qui montre le résultat

> **Commit** : `feat(admin): page Traits et répliques`
> **Phase** : le bureau des agents
> **Fichiers** : `Admin/src/app/pages/lines/`, routes et menu

## 1. Ce qu'on construit

Configuration › **Traits et répliques**, en deux colonnes :

- **Traits** : une clé, un libellé, la phrase ajoutée à la consigne. On en ajoute, on en retire, on enregistre.
- **Répliques** : pour chaque événement, qui parle, le texte, les variables disponibles (un clic les insère), et la **bulle telle qu'elle s'affichera**, remplie avec des valeurs d'exemple.

## 2. Valider deux fois, pour deux raisons

L'écran signale une variable inconnue **pendant la frappe** (bordure rouge, message) : c'est pour le confort. Le serveur la refuse **à l'enregistrement** (leçon 73) : c'est pour la sécurité, parce qu'un appel à l'API peut venir d'ailleurs que de cet écran. Les deux utilisent la même règle.

## 3. Revenir au départ

« Réplique de départ » envoie `null` pour cet événement : le serveur efface la ligne, et la réplique d'origine reprend. Rien n'est perdu, et l'on n'a pas besoin de connaître le texte d'origine pour y revenir.

## 4. N'envoyer que ce qui a changé

L'écran garde les modifications dans un dictionnaire (`edits`). « Enregistrer » n'envoie que celles-là, et reste grisé tant qu'il n'y en a aucune.

## 5. Quiz

1. Pourquoi valider les variables dans l'écran **et** sur le serveur ?
2. Que fait « Réplique de départ » ?

<details>
<summary>Réponses du quiz</summary>

1. L'écran aide pendant la frappe ; le serveur protège, car l'API peut être appelée sans l'écran.
2. Elle envoie null : le serveur supprime la réplique modifiée, et celle d'origine reprend.

</details>
