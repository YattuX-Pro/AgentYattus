# Leçon 37 — Séparer gérer et travailler : l'espace admin web

> **Commit** : `docs(adr): espace d'administration web, séparé des apps de travail`
> **Phase** : espace admin web
> **Fichiers** : `docs/adr/0008-espace-admin-web.md`, `docs/design/admin-web.html`

## 1. Ce qu'on décide

Agent Yattus aura **deux sortes d'interfaces** :

| Interface | Pour qui | Ce qu'on y fait |
|---|---|---|
| **Espace admin web** (Angular) | Un ou deux administrateurs | Tout gérer : modèles, agents, missions, audit, puis projets et utilisateurs |
| **Bureau et mobile** | Toute l'équipe | Travailler : lancer une mission, la suivre, approuver, lire le rapport |

La maquette validée est dans `docs/design/admin-web.html`.

> **Une image** : dans un restaurant, les cuisiniers ont leur poste de travail, le gérant a son bureau. Le gérant ne range pas les fiches de paie au-dessus des fourneaux, et le cuisinier n'a pas besoin du coffre pour cuire un plat.

## 2. Pourquoi séparer

- **Chaque écran sert une seule personne** : un développeur qui lance une revue n'a pas à voir la gestion des clés d'API.
- **On ne fait les choses qu'une fois** : sans séparation, il faudrait coder la gestion des modèles dans le bureau, puis dans le mobile, puis sur le web.
- **La sécurité suit naturellement** : l'espace admin est réservé à un rôle ; les apps de travail n'ont jamais besoin des routes d'administration.

C'est le principe de **séparation des responsabilités** (*separation of concerns*), appliqué aux applications et plus seulement aux classes.

## 3. Lire un ADR

Un **ADR** (*Architecture Decision Record*) garde la trace d'une décision qui sera difficile à défaire. Il répond à quatre questions :

1. **Contexte** : quel problème, quelles contraintes ?
2. **Décision** : ce qu'on fait, précisément.
3. **Alternatives** : ce qu'on a écarté, et pourquoi (le tableau « Pour / Contre »).
4. **Conséquences** : ce que la décision coûte.

Six mois plus tard, quand quelqu'un demandera « pourquoi pas Blazor ? », la réponse est écrite.

## 4. Les choix de l'ADR 0008

- **Angular + spartan/ui** : spartan copie ses composants stylés **dans notre projet**. On les possède : on les plie à notre palette au lieu de lutter contre un thème imposé.
- **Angular 21** et non 22 : la version 22.2 exige Node 24.15, la machine a Node 24.13. Une contrainte réelle, notée, avec la condition pour changer.
- **Jetons en mémoire** : `localStorage` est lisible par n'importe quel script de la page. Si une faille XSS en injecte un, il repart avec le jeton. En mémoire, le jeton disparaît au rechargement, et Keycloak reconnecte sans mot de passe tant que sa session dure.
- **Pas de CORS** : en développement, le serveur Angular relaie les appels à l'API. Pour le navigateur, tout vient de la même origine.

⚠️ **Masquer un bouton n'est pas une sécurité.** L'espace admin vérifie le rôle pour afficher ses pages, mais c'est l'API qui refuse, route par route. N'importe qui peut appeler l'API sans passer par l'écran.

## 5. Quiz

1. Pourquoi ne pas mettre la gestion des modèles dans l'app mobile aussi ?
2. Quelles sont les quatre parties d'un ADR ?
3. Pourquoi les jetons ne vont-ils pas dans `localStorage` ?
4. Pourquoi le serveur de dev d'Angular relaie-t-il les appels à l'API ?

## 6. Pour aller plus loin

- Les ADR selon Michael Nygard : <https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions>
- spartan/ui : <https://www.spartan.ng>
- Où stocker un jeton dans le navigateur (OWASP) : <https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html#local-storage>

<details>
<summary>Réponses du quiz</summary>

1. La gestion est le travail d'un ou deux administrateurs ; l'écrire une seule fois, sur le web, évite trois versions à maintenir et allège les apps de travail.
2. Contexte, décision, alternatives, conséquences.
3. Tout script de la page peut lire `localStorage` : une faille XSS suffirait à voler le jeton.
4. Pour que le navigateur voie une seule origine : l'API n'a pas besoin de CORS et reste sur la boucle locale.

</details>
