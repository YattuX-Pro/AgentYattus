# Leçon 129 — Le chef d'équipe est un collègue recruté

> **Commit** : `feat(team): le chef d'équipe est un collègue recruté, désigné au recrutement`
> **Phase** : hors plan (demande du 7 novembre, suite de la leçon 128)
> **Maquette** : « Bureau des agents », artboards Recrutement, Accueil, Agents (l'interrupteur et la pastille n'y sont pas encore)
> **Fichiers** : `Contracts/{Agents,Missions,Team}.cs`, `Api/Modules/Team/{TeamDb,Colleagues,TeamContent,TeamModule,PortraitSeed}.cs` + migration `ColleagueLeader`, `Api/Modules/Agents/{AgentRoles,AgentFrameworkRunner,AgentsModule}.cs`, `Api/Modules/Missions/{MissionsModule,MissionWorker}.cs`, `Client.Core/{HomeViewModel,OfficeViewModel,Scene,RecruitViewModel,AgentsViewModel,MissionItem,FaceBook}.cs`, `Desktop/Views/{RecruitView,AgentsView}.axaml`, `Admin/src/app/pages/teams/*`, `deploy/portraits/portraits.json`

## 1. Ce qu'on a construit

À la leçon 128, le chef d'équipe était un **personnage à part**. Il avait une clé fixe (`"chef"`), un prénom et un portrait réglés dans l'admin (Léon), et un seul outil, `confier`. Il apparaissait à l'accueil et dans l'open space, mais pas sur l'écran Agents. Résultat : un agent que personne n'avait recruté.

Maintenant :

- **Il n'y a que les agents recrutés.** Le chef d'équipe est l'un d'eux.
- **Au recrutement, un interrupteur « Chef d'équipe »** dit si c'est lui à qui l'on s'adresse par défaut. Le badge affiche alors « CHEF D'ÉQUIPE · ANALYSTE ».
- **Un seul chef par projet.** En recruter un nouveau avec l'interrupteur retire le titre à l'ancien, qui reste dans l'équipe comme simple collègue.
- **Le chef garde son poste.** Inès, Analyste et cheffe, lit les fichiers elle-même si la demande relève de son poste. Sinon, elle confie la demande à Malik avec `confier`, et Malik répond directement.
- **L'équipe de départ** (admin › Équipes) a le même interrupteur, sur un siège au plus. La galerie importée fait d'Inès la cheffe.

## 2. Le problème

Un rôle de **répartition** (le chef) et un **poste** (Analyste, Rédacteur…) sont deux choses différentes. La leçon 128 avait fait du chef un poste invisible. Mais l'utilisateur pense « équipe » : tous ceux qui travaillent ont été recrutés, et l'un d'eux encadre. Un agent fantôme, qu'on ne peut ni voir dans Agents, ni remplacer, ni régler, contredit ce modèle.

## 3. Les options

| Option | Principe | Pour | Contre |
|---|---|---|---|
| A. Garder le chef à part, le montrer dans Agents | Une fiche de plus | Rien à migrer | Toujours un agent non recruté ; deux façons de régler un agent |
| B. **Un drapeau sur le collègue** | `Colleague.Leader`, choisi au recrutement | Un seul modèle : « l'équipe » ; le chef garde ses outils | Migration ; le chef cumule deux consignes |
| C. Un poste « Chef » à recruter | Un rôle de plus dans le catalogue | Simple à afficher | Un chef qui ne sait que répartir occupe un siège de plus |

**Choix : B.** C'est la demande (« lors de la création d'un agent on doit préciser si c'est lui le chef d'équipe »). Ça garde la répartition comme une **capacité en plus**, pas comme un agent en plus.

## 4. Le code expliqué

### La donnée (module Équipe)

- `Colleague.Leader` (booléen) et un **index unique filtré** : `(ProjectId) WHERE "Active" AND "Leader"`. C'est la base qui garantit « un seul chef actif par projet », même si deux recrutements se croisent. C'est le même procédé que l'index des sièges (`IX_Colleagues_Seat`).
- `POST /projects/{id}/colleagues` reçoit `Leader`. S'il est vrai, on retire d'abord le titre à l'ancien chef. On enregistre en **deux temps**, sinon l'index refuserait le nouveau chef tant que l'ancien l'est encore.
- `TeamSettingsDto` perd `LeaderName` et `LeaderPortraitId`. `DefaultSeatDto` gagne `Leader`, et la validation refuse deux chefs dans l'équipe de départ.
- La migration `ColleagueLeader` supprime les deux colonnes. Pour que les équipes existantes gardent un chef, elle nomme chef **l'Analyste en place**, et coche le siège Analyste de l'équipe de départ (un `regexp_replace` sur le JSON).

### Le routage (module Agents)

`AgentSeat` (ce que le module Équipe dit d'un collègue aux missions) gagne `Leader`. Dans `AgentFrameworkRunner.RunAsync`, pour une **question** :

1. Si l'agent de l'étape est le chef du projet, `TeamAsync` liste **les autres collègues recrutés** (lui exclu).
2. `RunRoleAsync(…, team)` compose la consigne : celle du poste, puis la présentation, puis `AgentRoles.Lead(team)` (« Tu es aussi le chef d'équipe… Ton équipe : … »), puis les garde-fous. `confier` est ajouté à **ses** outils, et les garde-fous le citent.
3. `confier` note le choix dans `_handoff` et arrête la boucle (`Terminate`, leçon 128).
4. Si `_handoff` est rempli, on note `delegue:<clé>`, et le collègue repart de la demande et de la consigne du chef.

Une revue suit son circuit, sans répartition. Un collègue choisi en direct n'a jamais `confier`.

`RolesFor` (le budget) compte, pour une question au chef, **tous les collègues du projet** : il peut leur confier la demande. On lit donc les sièges **avant** le contrôle du budget dans `MissionWorker`.

`"chef"` n'est plus une clé d'agent. `POST /missions` avec `agent: "chef"` est refusé (400).

### Le client

- **Accueil** : `Crew` = les collègues actifs du projet, le chef en premier. Sans choix, la question part au chef ; sans chef, au premier collègue ; sans équipe, `agent: null` (l'Analyste, côté serveur).
- **Recrutement** : `RecruitViewModel.IsLeader`, repris de `current.Leader` quand on change le visage du chef (sinon, il perdrait le titre sans le vouloir).
- **Agents** : une pastille « CHEF D'ÉQUIPE » en vert-de-gris, à gauche de « PERSO ».
- **Open space** : le bureau du chef reçoit le collègue chef. `DeskOf` le fait parler depuis ce bureau, quel que soit son poste. Son bureau de poste reste « Siège libre » : chacun n'apparaît qu'une fois. Sans chef, personne n'annonce le début ni la fin (`Scene.From`).

## 5. Vérifie toi-même

```bash
dotnet test tests/AgentYattus.Api.Tests --filter-class "*AgentRunnerTests"
```

```bash
dotnet test tests/AgentYattus.Api.Tests --filter-class "*TeamTests"
```

```bash
dotnet test tests/AgentYattus.Client.Core.Tests
```

- `The_leader_hands_the_request_to_a_colleague_who_answers_directly` : Inès (Analyste, cheffe) a `list_files`, `read_file` **et** `confier`. Elle ne se voit pas dans son équipe, et confie au Rédacteur, qui n'a pas `confier`.
- `The_leader_answers_himself_with_his_own_tools` : elle liste les fichiers elle-même, sans `delegue:`.
- `A_colleague_spoken_to_directly_cannot_hand_over_and_the_old_fixed_leader_is_gone` : `agent: "chef"` est refusé.
- `The_leader_is_chosen_when_recruiting_and_there_is_only_one_per_project` : le titre passe d'Inès à Malik. Remplacer Malik sans cocher laisse le projet sans chef.

Dans le logiciel : recrute un collègue avec « Chef d'équipe » coché. À l'accueil, il passe en premier, avec « CHEF D'ÉQUIPE » sous son visage.

## 6. Les limites (assumées)

- **Pas encore vérifié sur PostgreSQL** : la migration (et son `regexp_replace`) a été relue en SQL (`dotnet ef migrations script`), pas exécutée. Les tests tournent sur SQLite.
- **Changer de chef = recruter.** Il n'y a pas de bouton « nommer chef » sur une fiche. On passe par « Nouveau visage » avec l'interrupteur.
- **L'open space a trois bureaux fixes.** Quand le chef est l'Analyste, le bureau de l'Analyste reste libre, et le dossier des archives vole encore vers lui.
- **Maquette** : l'interrupteur du recrutement et la pastille de l'écran Agents ne sont pas encore dessinés dans « Bureau des agents ».

## 7. Quiz

1. Pourquoi un index unique filtré plutôt qu'un simple contrôle dans le code ?
2. Pourquoi enregistrer en deux temps quand on nomme un nouveau chef ?
3. Pourquoi le chef ne figure-t-il pas dans la liste de sa propre équipe ?
4. Pourquoi le budget compte-t-il tous les collègues pour une question au chef ?

<details>
<summary>Réponses</summary>

1. Deux recrutements simultanés passeraient tous les deux le contrôle du code. La base, elle, refuse le second.
2. Tant que l'ancien chef garde `Leader = true`, insérer le nouveau viole l'index : on retire d'abord le titre, puis on ajoute le collègue.
3. `confier` vers lui-même relancerait la même demande avec la même consigne : un tour de modèle pour rien. S'il peut le faire, il répond lui-même.
4. Le chef peut confier la demande à n'importe lequel d'entre eux. Si l'un utilise un modèle payant, la mission peut coûter, même si le chef est gratuit.

</details>
