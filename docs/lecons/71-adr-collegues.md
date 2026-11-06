# Leçon 71 — Décider avant de coder : les agents comme collègues

> **Commit** : `docs(adr): ADR 0012, les collègues`
> **Phase** : le bureau des agents
> **Fichiers** : `docs/adr/0012-collegues.md`

## 1. Ce qu'on décide

Le bureau va montrer une mission comme un **open space** : des collègues avec un visage, qui se parlent dans des bulles et se passent des dossiers. L'ADR fixe ce qu'est un « collègue » avant d'écrire une ligne : **un rôle existant + un portrait + un prénom + des traits**.

## 2. Séparer le contenu du comportement

La contrainte de départ : **rien en dur**. Le tri est simple :

| Contenu (une donnée, dans l'admin) | Comportement (du code) |
|---|---|
| les visages, les prénoms, l'équipe de départ | quel événement fait parler qui |
| les traits et leur phrase de style | quel objet circule entre les bureaux |
| les phrases des bulles | les animations |
| la vitesse de la scène | les règles de sécurité |

Un texte qu'on voudra changer sans redéployer est une donnée. Une règle qui protège le système reste du code : sinon, une erreur de saisie dans l'admin suffirait à la retirer.

## 3. Trois choix par défaut, et leur raison

- **Les outils sont hérités du rôle.** Recruter une analyste ne peut pas lui donner `write_file` : la sécurité ne dépend pas d'un écran de recrutement.
- **Les bulles viennent des événements.** « Je lis OrderService.cs » est vrai, parce que l'agent l'a vraiment lu. Une phrase écrite par le modèle coûterait des tokens et pourrait être fausse.
- **Un siège par rôle et par projet.** C'est ce que montre l'open space, et cela évite de devoir choisir « quelle analyste » pour chaque mission.

## 4. Quiz

1. Pourquoi les outils d'un collègue ne se choisissent-ils pas au recrutement ?
2. Pourquoi ne pas faire écrire les bulles par le modèle ?

<details>
<summary>Réponses du quiz</summary>

1. Les outils touchent à la sécurité : ils restent attachés au rôle, réglé par un administrateur, et imposés par le code.
2. Chaque bulle coûterait des tokens, et la phrase pourrait décrire une action qui n'a pas eu lieu.

</details>
