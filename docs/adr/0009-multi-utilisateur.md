# ADR 0009 — Plusieurs utilisateurs : organisations, projets et partage

- **Statut** : proposé
- **Date** : 2026-11-06

## Contexte

Jusqu'ici, Agent Yattus sert une seule équipe qui partage tout : n'importe quel compte connecté voit toutes les missions, et une seule configuration vaut pour tous. L'équipe va grandir. Chacun a ses projets, et ne veut les montrer qu'à qui il choisit : une personne, ou toute une organisation.

## Décision

1. **Keycloak reste la source des identités** (comptes, mots de passe, MFA). L'application tient un **annuaire** : à chaque requête authentifiée, elle enregistre l'utilisateur (identifiant `sub`, nom d'utilisateur, nom affiché, date de dernière visite). On ne peut partager qu'avec quelqu'un qui s'est connecté au moins une fois. Les comptes se créent dans la console Keycloak.
2. **Organisations.** Une organisation regroupe des membres, chacun avec un rôle :

   | Rôle | Peut |
   |---|---|
   | **Administrateur** | gérer l'organisation et ses membres ; modifier tous ses projets |
   | **Opérateur** | créer des projets dans l'organisation ; lancer des missions là où il est éditeur |
   | **Observateur** | voir les projets ouverts à l'organisation |

   Créer une organisation est réservé aux **administrateurs de l'instance** (rôle Keycloak `llm-admin`), qui deviennent son premier administrateur.
3. **Projets.** Un projet a un **propriétaire**, appartient à une organisation ou à l'espace personnel de son propriétaire, et a une **visibilité** :

   | Visibilité | Qui y accède |
   |---|---|
   | **Privé** | le propriétaire seul |
   | **Partagé** | le propriétaire et les personnes invitées (Éditeur ou Lecteur) |
   | **Organisation** | en plus, tous les membres de l'organisation, en lecture |

   Un **éditeur** lance des missions et décide des écritures ; un **lecteur** voit les missions et leurs rapports. Les administrateurs de l'organisation sont éditeurs de tous ses projets.
4. **Les missions appartiennent à un projet** et gardent leur auteur. Sans projet précisé (le bureau d'aujourd'hui), la mission va dans le **projet personnel** de l'auteur, créé à la première mission. On ne voit que les missions des projets accessibles. Une mission inaccessible répond **404**, pas 403, pour ne pas révéler qu'elle existe.
5. **Les administrateurs de l'instance voient tout, sans tout pouvoir.** Ils lisent toutes les missions et le journal global : l'administration et l'audit l'exigent. En revanche, ils ne lancent rien et ne décident rien dans un projet privé qui n'est pas le leur. La lecture reste visible, puisque l'admin web est un outil d'administration.
6. **Le dossier de travail est propre à chaque projet**, et **seul un administrateur de l'instance le choisit**. Laisser chaque utilisateur désigner un dossier du serveur lui donnerait accès à tout ce que l'API peut lire. Sans dossier choisi, c'est le dossier configuré (`Agents:Workspace`).
7. **Le direct suit les droits.** Le hub SignalR n'envoie plus un événement à tous les clients : il l'envoie aux seuls utilisateurs qui peuvent lire la mission.

## Reporté (une décision chacun, plus tard)

- **Clés de modèle personnelles** (une clé qui ne sert qu'aux projets de son propriétaire) et un modèle par projet : la résolution du modèle change de nature.
- **Budget** par organisation et par projet.
- **Transfert de propriété** d'un projet, et organisations créées par n'importe quel utilisateur.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Rôles dans Keycloak (groupes, rôles par client) | Une seule source de vérité | Un projet partagé avec trois personnes = un groupe Keycloak ; la gestion quitte l'application |
| **Identité Keycloak, droits dans l'application** | Les droits fins vivent avec les données qu'ils protègent | Un annuaire à tenir à jour |
| Inviter par adresse e-mail, comptes créés par l'API | Inviter sans passer par la console | Il faut un compte de service Keycloak avec des droits d'administration : une clé très sensible de plus |
| Admin d'instance tout-puissant | Simple | Un projet « privé » ne le serait pas vraiment |
| 403 pour une mission inaccessible | Plus explicite | Révèle qu'elle existe |

## Conséquences

- Deux modules de plus : **Identity** (annuaire, organisations) et **Projects** (projets, partage, droits). Missions en dépend, jamais l'inverse.
- Chaque route des missions vérifie les droits : les tests couvrent un deuxième utilisateur qui ne doit rien voir.
- Les missions créées avant cette version n'ont pas de projet : seuls les administrateurs de l'instance les voient.
