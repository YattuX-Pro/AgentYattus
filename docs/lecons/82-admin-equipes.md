# Leçon 82 — La page Équipes : régler sans redéployer, et voir le réglage agir

> **Commit** : `feat(admin): page Équipes`
> **Phase** : le bureau des agents
> **Fichiers** : `Admin/src/app/pages/teams/`, `shared/portrait-picker.ts`, routes et menu

## 1. Ce qu'on construit

Administration › **Équipes** :

- le **chef d'équipe** (visage et prénom) et le portrait de **« Vous »** ;
- l'**équipe de départ** : un visage et un prénom par rôle, ou un siège laissé vide ;
- le **rythme de la scène** : millisecondes par lettre et temps de lecture, avec un **aperçu** qui s'écrit à la vitesse choisie ;
- les **collègues des projets** : qui, dans quel projet, recruté par qui, et ceux qu'ils ont remplacés.

## 2. Un composant partagé, pas trois copies

`PortraitPicker` (une rangée de visages, un choix) sert au chef, à « Vous » et à chaque siège. C'est un `radiogroup` : au clavier, chaque visage est un bouton annoncé par son prénom.

## 3. Montrer l'effet d'un réglage

« 38 ms par lettre » ne dit rien à personne. L'aperçu rejoue une phrase à cette vitesse dès qu'on change la valeur : on règle ce qu'on voit. La fonction qui calcule le nombre de lettres est pure (`typedLength`) et testée seule.

## 4. Retirer un collègue : les droits du projet

Le bouton « Retirer » appelle la même route que le bureau. Un administrateur de l'instance **lit** toutes les équipes, mais ne retire un collègue que dans un projet où il a le droit d'agir (ADR 0009) ; sinon le serveur refuse et l'admin affiche pourquoi.

## 5. Quiz

1. Pourquoi un aperçu animé pour un réglage de vitesse ?
2. Pourquoi un administrateur de l'instance ne peut-il pas retirer un collègue partout ?

<details>
<summary>Réponses du quiz</summary>

1. Une valeur en millisecondes ne se ressent pas ; la voir s'écrire permet de choisir.
2. Il lit tout, mais n'agit pas dans les projets privés des autres : c'est la règle de l'ADR 0009, appliquée par le serveur.

</details>
