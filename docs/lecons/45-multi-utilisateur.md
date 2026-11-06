# Leçon 45 — Plusieurs utilisateurs : qui possède quoi

> **Commit** : `docs(adr): plusieurs utilisateurs, organisations, projets et partage`
> **Phase** : multi-utilisateur
> **Fichiers** : `docs/adr/0009-multi-utilisateur.md`

## 1. Ce qu'on décide

Avant d'écrire une ligne de code, on fixe **qui possède quoi** :

| Élément | Appartient à |
|---|---|
| Comptes, mots de passe | Keycloak |
| Organisations et rôles de leurs membres | l'application (module Identity) |
| Projets, visibilité, invités | le propriétaire du projet (module Projects) |
| Missions et rapports | le projet |
| Dossier de travail | le projet, choisi par un administrateur de l'instance |

> **Une image** : un immeuble de bureaux. L'accueil (Keycloak) vérifie ton badge. Chaque entreprise (organisation) décide qui entre dans ses locaux. Dans chaque bureau (projet), son occupant choisit qui peut entrer et qui peut seulement regarder par la vitre.

## 2. Authentification et autorisation : deux questions

- **Authentification** : *qui es-tu ?* Keycloak y répond et signe un jeton.
- **Autorisation** : *as-tu le droit ?* La réponse dépend des données : ce projet, ses invités, son organisation. Elle vit donc dans l'application, à côté des données.

C'est pourquoi on ne crée pas un groupe Keycloak par projet partagé. Les droits fins restent avec ce qu'ils protègent.

## 3. Trois visibilités

| Visibilité | Qui y accède |
|---|---|
| Privé | le propriétaire seul |
| Partagé | le propriétaire et ses invités (Éditeur ou Lecteur) |
| Organisation | en plus, tous les membres de l'organisation, en lecture |

**Éditeur** : lance des missions, décide des écritures. **Lecteur** : regarde.

## 4. 404 plutôt que 403

Une mission d'un projet privé, demandée par quelqu'un d'autre, répond **404 Introuvable**, pas **403 Interdit**. Un 403 dirait « elle existe, mais pas pour toi ». C'est déjà une information.

## 5. Le principe du moindre privilège, même pour l'admin

Les administrateurs de l'instance **lisent** tout (l'audit l'exige), mais ne **décident** rien dans le projet privé d'un autre. Et le dossier de travail d'un projet se choisit seulement par eux : sinon, n'importe qui ferait lire aux agents n'importe quel dossier du serveur.

## 6. Ce qui est reporté, et pourquoi c'est écrit

Clés de modèle personnelles, budget par projet, transfert de propriété : chacun aura sa décision. Les écrire dans l'ADR évite que quelqu'un croie qu'on les a oubliés.

## 7. Quiz

1. Pourquoi les droits sur les projets ne sont-ils pas dans Keycloak ?
2. Pourquoi répondre 404 plutôt que 403 pour la mission d'un autre ?
3. Pourquoi un utilisateur ne peut-il pas choisir le dossier de travail de son projet ?

<details>
<summary>Réponses du quiz</summary>

1. Parce qu'ils dépendent des données (projet, invités, organisation) : ils vivent avec elles. Keycloak répond seulement à « qui es-tu ».
2. Un 403 révèle que la mission existe.
3. Il pourrait désigner n'importe quel dossier lisible par le serveur, et les agents le liraient pour lui.

</details>
