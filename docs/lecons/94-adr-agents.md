# Leçon 94 — Ajouter des agents : décider ce qui reste fermé

> **Commit** : `docs(adr): ADR 0013, ajouter des agents depuis l'admin`
> **Phase** : le bureau des agents
> **Fichiers** : `docs/adr/0013-agents-personnalises.md`

## 1. Ce qu'on décide

On peut désormais créer un agent dans l'admin : un nom, une présentation, une consigne, et des outils **choisis dans la liste existante**. On le choisit au lancement d'une mission « Question », et il la fait seul.

## 2. Ouvrir sans tout ouvrir

| Ouvert (formulaire) | Fermé (code) |
|---|---|
| le nom, la présentation, la consigne | la liste des outils qui existent |
| quels outils parmi ceux-là | l'accord humain avant toute écriture |
| le modèle, le portrait, le siège | les garde-fous ajoutés à chaque consigne |

Un outil est du code qui touche au disque : il passe par une revue de code, jamais par un formulaire. L'accord humain dépend de l'outil (`write_file`), pas de l'agent : aucun réglage d'agent ne peut le retirer.

## 3. Le plus simple des trois circuits

Choisir l'agent au lancement est le circuit le plus prévisible : un agent, une mission, un coût. La chaîne d'agents et le chef qui délègue restent possibles plus tard ; ils feront chacun l'objet de leur propre décision.

## 4. Quiz

1. Pourquoi les outils se choisissent-ils dans une liste fermée ?
2. Pourquoi l'accord humain ne fait-il pas partie des réglages d'un agent ?

<details>
<summary>Réponses du quiz</summary>

1. Un outil est du code qui agit sur le disque : en ajouter un exige une revue, pas un formulaire.
2. Il est attaché à l'outil d'écriture, dans le code : aucun agent créé ne peut écrire sans votre décision.

</details>
