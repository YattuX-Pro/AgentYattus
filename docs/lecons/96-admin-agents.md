# Leçon 96 — La page Agents : moins de texte, un bouton « Nouvel agent »

> **Commit** : `feat(admin): page Agents allégée, créer et retirer un agent`
> **Phase** : le bureau des agents
> **Fichiers** : `Admin/src/app/pages/agents/` (`agents.html`, `agents.ts`, `tools.ts`, spec), `core/api.ts`

## 1. Ce qui n'allait pas

Retour de l'utilisateur : impossible d'**ajouter** un agent, et **trop de texte** : des statistiques en quatre colonnes, un tableau des outils qui répétait les cartes, des explications sous chaque champ.

## 2. Ce qu'on garde, ce qu'on enlève

- Une carte par agent : son nom, ce qu'il fait (deux lignes au plus), ses outils en **pastilles** (icône et mot simple : « Lire les fichiers », « Écrire, avec votre accord » en laiton), son modèle et son nombre de missions sur une seule ligne.
- Le tableau des outils disparaît : les pastilles le disent déjà.
- Les règles ajoutées par le code se replient dans un `<details>` : elles restent consultables, sans encombrer.

## 3. Créer un agent

« Nouvel agent » ouvre un panneau : nom, ce qu'il fait, ce qu'il peut faire (trois boutons à bascule), consigne. « Créer l'agent » reste grisé tant que les règles du serveur ne sont pas remplies : une fonction pure (`formErrors`), testée seule, les applique dans l'écran.

Pour un agent créé, le panneau « Modifier » ajoute son profil (nom, présentation, outils) et « Retirer l'agent », en deux clics : le second confirme.

## 4. Quiz

1. Pourquoi supprimer le tableau des outils ?
2. Pourquoi retirer un agent demande-t-il deux clics ?

<details>
<summary>Réponses du quiz</summary>

1. Il répétait ce que les pastilles de chaque carte disent déjà : moins de texte, la même information.
2. Pour éviter un retrait par erreur, sans fenêtre de confirmation de plus : le bouton change de texte et attend un second clic.

</details>
