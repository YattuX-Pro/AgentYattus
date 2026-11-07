# Leçon 93 — Bilan : le bureau des agents

> **Commit** : `docs: README, sécurité, feuille de route et cours (leçons 71 à 93)`
> **Phase** : le bureau des agents

## 1. Ce que la phase a construit

- **Un module Équipe** : galerie de portraits en base, équipe de chaque projet (un siège par rôle), traits, répliques et rythme de la scène, tous gérés dans l'admin.
- **L'open space du bureau** : des collègues réalistes (générés avec Codex), des bulles qui s'écrivent, des dossiers qui circulent, une porte à laquelle on frappe, un fil d'équipe.
- **Le recrutement** depuis le bureau : un visage, un prénom, un poste, des traits.
- **Un admin qui bouge** : transitions sur chaque clic et chaque page, placeholders partout, un menu latéral refait.

Juste avant la phase, un commit à part a refait la **page de connexion** (thème Keycloak en deux colonnes, même écran sur le bureau, Google et GitHub par Keycloak).

## 2. Les fils rouges

1. **Le contenu dans l'admin, le comportement dans le code.** Tout ce qu'on voudra changer sans redéployer est une donnée ; les règles de sécurité restent du code.
2. **Le temps comme paramètre.** La scène se teste sans interface : `Tick(temps écoulé)`.
3. **La décision ne change pas.** Une scène plus agréable n'autorise jamais à approuver sans avoir tout lu.
4. **Ce qui vient du modèle est une entrée hostile**, même un simple chemin de fichier qui part dans un journal (leçon 92).

## 3. Ce qui reste

Le poste « personnalisé » (rôles sur mesure), et les statuts des bureaux réglables dans l'admin. La feuille de route les garde.

## 4. Quiz

1. Citez une chose de la phase qui est une donnée de l'admin, et une qui est restée du code. Pourquoi ?
2. Qu'est-ce qui garantit qu'on ne peut pas approuver une écriture depuis la carte « on frappe à votre porte » sans l'avoir lue ?

<details>
<summary>Réponses du quiz</summary>

1. Les répliques sont une donnée (on veut les changer sans redéployer) ; la règle « approuver après avoir tout affiché » est du code (une erreur de saisie ne doit pas pouvoir la retirer).
2. La carte ouvre seulement la relecture ; « Approuver » y est désactivé tant que tout le contenu n'a pas été affiché, avec la même règle que MissionViewModel.

</details>
