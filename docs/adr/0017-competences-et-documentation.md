# ADR 0017 — Compétences gouvernées et documentation consultée

- **Statut** : proposé
- **Date** : 2026-11-07

## Contexte

Les compétences (leçon 104) étaient un texte modifiable en place, attaché à un collègue, chargé à la demande (`use_skill`). Il manquait ce qui permet de leur faire confiance dans un parcours de développement : savoir **quelle version** une mission a suivie, **valider** avant usage, **choisir** une compétence pour une mission, et lui associer des **références** que l'agent consulte réellement (documentation du dépôt, documentation officielle).

Quatre notions restent distinctes :

| Notion | Ce que c'est | Où |
|---|---|---|
| **Compétence** | une méthode à suivre | module Agents, `Skills` |
| **Documentation** | une information de référence, lue au besoin | `read_file` (dépôt), `read_documentation` (web) |
| **Outil** | une capacité technique, décidée par le rôle | `AgentRoles`, code |
| **Mémoire du projet** | les décisions propres à un projet | pas encore (voir la feuille de route) |

## Décision

1. **Une compétence a une version** : chaque modification en crée une (`SkillVersions`), restaurer une ancienne en crée une nouvelle. Le journal note la version chargée (`version:competence:clé:n`).
2. **Un statut** : brouillon (importée : aucun agent ne la lit), approuvée, dépréciée (encore lue, signalée comme telle). Retirée, elle n'existe plus pour personne.
3. **L'import d'un `SKILL.md`** : l'en-tête `name` et `description` du format « Agent Skills » d'Anthropic, plus nos champs `references`, `technologies` et `tools`. **Du texte seulement** : un dossier de compétence peut contenir des scripts, ils ne sont ni importés ni exécutés. `tools` est affiché, **jamais accordé**.
4. **Avant de valider** : un secret dans la procédure est refusé ; des consignes risquées (« ignore tes règles », « sans demander », `curl … | sh`, `.env`) sont signalées à la personne qui valide, qui voit tout le contenu et la portée. Une heuristique : elle aide, elle ne décide pas.
5. **Des références** (dix au plus) : un chemin relatif du dépôt, ou une page `https` sur un site de documentation autorisé. L'agent les reçoit avec la procédure et ne les consulte **qu'au besoin**.
6. **Choisir des compétences pour une mission** (cinq au plus, approuvées, de l'instance ou à soi) : elles sont données **en entier** à chaque agent de la mission, avec la règle de conflit : la demande de l'humain, puis ces compétences, puis celles du collègue ; jamais avant la sécurité, et l'agent signale le conflit.
7. **`read_documentation`** : `https`, port 443, pas d'adresse IP, pas d'identifiant, sites autorisés (`Agents:DocumentationHosts`, une liste officielle par défaut). Chaque redirection est revérifiée ; la connexion ne part que vers une **adresse publique**, vérifiée au moment de se connecter (contre le « DNS rebinding ») ; ni proxy, ni cookies ; 2 Mo, 15 s, HTML, Markdown ou texte ; 20 pages par mission. Le texte rendu porte sa date, son empreinte et la version indiquée par l'adresse, et se présente comme une **donnée**.
8. **Le résultat cite ce qui a réellement servi** : compétences (version, choisie ou chargée), consignes du dépôt, pages consultées (date, empreinte, version), pages **non** consultées (et pourquoi), fichiers lus. Rien n'est inventé par le modèle.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Toutes les compétences dans chaque conversation | Simple | Coûteux, et l'agent suit des règles hors sujet |
| Une recherche web libre | Trouve tout | Des sources non officielles ; les requêtes peuvent emporter du contenu interne |
| Un index documentaire (RAG) | Rapide sur de gros corpus | Infrastructure, droits sur l'index, invalidation : pas avant d'en avoir besoin |
| **Choix explicite + index léger + pages officielles autorisées** | Peu de code, vérifiable, cité | Pas de recherche : l'agent doit connaître l'adresse (ou la compétence la donne) |

## Conséquences

- (+) On sait ce qu'une mission a suivi et lu, version et empreinte comprises.
- (+) Une compétence importée ne sert à rien tant qu'un humain ne l'a pas relue.
- (−) Pas encore de compétence **de projet** ni **d'organisation** : personnelle, ou de l'instance.
- (−) Une compétence « cachée » est donnée au modèle : il peut la répéter. Ce n'est pas un coffre ; ne jamais y mettre de secret.
- (−) Pas d'instantané documentaire hors ligne ni de proposition de mise à jour quand une page change : l'empreinte est relevée, la comparaison viendra.
- (−) La mesure reste le nombre de chargements et la réussite des missions ; comparer avec et sans compétence demande les évaluations sur de vrais modèles (payantes, désactivées par défaut).
