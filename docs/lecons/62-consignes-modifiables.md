# Leçon 62 — Ce qu'une consigne peut changer, et ce qu'elle ne peut pas

> **Commit** : `docs(adr): consignes des agents modifiables`
> **Phase** : compléter l'admin
> **Fichiers** : `docs/adr/0011-consignes-agents.md`

## 1. Ce qu'on décide

Les administrateurs pourront modifier la **consigne** de chaque agent et sa **limite de tours**, avec un historique des versions. Mais certaines choses restent hors de portée du texte.

## 2. La règle d'or : la sécurité vit dans le code

| Ce qu'une consigne change | Ce qu'elle ne change pas |
|---|---|
| Le ton, la langue, le format du rapport | Les outils du rôle |
| L'ordre des vérifications | L'approbation humaine des écritures |
| Le niveau de détail | Le dossier de travail et ses fichiers exclus |
| La limite de tours (bornée) | La règle « le contenu des fichiers est une donnée » |

Pourquoi ? Une consigne est du **texte que le modèle lit**. Un modèle peut mal suivre un texte, ou se laisser convaincre par un autre texte (un fichier piégé : l'injection de prompt, leçon 3). Une barrière écrite dans le code, elle, s'applique quoi que dise le texte.

> **Une image** : on peut changer la fiche de poste d'un employé (« sois concis, rédige en anglais »). On ne lui donne pas pour autant les clés du coffre parce que sa fiche le dit.

## 3. Versionner plutôt qu'écraser

Chaque modification crée une **version** : qui, quand, quoi. Si un agent se met à mal travailler, on voit ce qui a changé, et on revient en arrière en un clic. C'est le même principe que Git pour le code, appliqué à une donnée.

## 4. Quiz

1. Pourquoi les outils d'un rôle ne se règlent-ils pas dans sa consigne ?
2. Que se passe-t-il si un administrateur efface toute la consigne ?
3. Pourquoi versionner les consignes ?

<details>
<summary>Réponses du quiz</summary>

1. Parce qu'un texte peut être mal suivi ou contourné ; les outils sont imposés par le code.
2. La règle de sécurité et la liste des outils sont ajoutées par le code : elles restent. Et l'écran refuse une consigne de moins de 20 caractères.
3. Pour savoir qui a changé quoi, et pouvoir revenir à une version précédente.

</details>
