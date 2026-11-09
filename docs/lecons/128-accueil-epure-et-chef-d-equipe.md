# Leçon 128 — L'accueil épuré, et le chef d'équipe qui répartit

> **Commit** : `feat(desktop): l'accueil épuré, et le chef d'équipe qui répartit le travail`
> **Phase** : hors plan (demande du 7 novembre), avance la case 140 de la phase E
> **Maquette** : « Bureau des agents », artboards `AccueilV2*` (« Accueil épuré »)
> **Fichiers** : `Contracts/Agents.cs`, `Api/Modules/Agents/{AgentRoles,AgentFrameworkRunner,AgentsModule}.cs`, `Api/Modules/Missions/MissionsModule.cs`, `Client.Core/{HomeViewModel,Folders,MissionItem,OfficeViewModel,Scene}.cs`, `Desktop/Views/HomeView.axaml(.cs)`, `Desktop/MainWindow.axaml(.cs)`

## 1. Ce qu'on a construit

Deux choses, demandées ensemble.

**Un accueil sans bruit.** On garde l'équipe, une phrase et le compositeur. Tout le reste a été remplacé :

| Avant | Maintenant |
|---|---|
| Un bandeau « DOSSIER DE TRAVAIL » et une phrase d'explication | Une pastille sous le compositeur : 📁 *Orders.Api* ▾, et ⑂ *main* si c'est un dépôt git |
| La boîte de dialogue du système pour choisir un dossier | Un panneau dans l'application : les projets, une zone « Glisser un dossier ici », et une navigation dans les dossiers |
| Une grille « RÉCENTES » sous le compositeur | Une bulle sur le visage de qui travaille (trois points) ou attend votre accord (la cloche), et une pastille ● 1 ◆ 1 en haut à droite qui ouvre le panneau des missions |
| Trois suggestions de mission | Rien : elles étaient génériques |
| Votre prénom à côté de votre portrait | Seulement le portrait ; le prénom est en tête de son menu |

On peut aussi glisser un dossier depuis le Finder **n'importe où sur la fenêtre** : un voile s'affiche, « Déposer pour travailler ici », avec le chemin et la branche.

**Le chef d'équipe.** Par défaut, on ne parle qu'à lui. Il répond lui-même à une question simple, ou il confie la demande au bon collègue, qui répond alors **directement**. Si l'on clique sur Inès, on lui parle en direct : le chef n'intervient pas, et son visage s'efface (opacité 0,45).

## 2. Le problème de fond : qui décide à qui va la demande ?

Avant, une mission « Question » allait à l'agent choisi, sinon à l'Analyste. L'utilisateur devait savoir qui fait quoi. Le schéma voulu est celui d'une vraie équipe : on s'adresse au chef, et c'est lui qui répartit.

C'est le motif **orchestrateur** : un agent ne fait pas le travail, il route la demande. Trois façons de le faire :

| Option | Principe | Pour | Contre |
|---|---|---|---|
| A. Routage par règles | Mots-clés, ou un classifieur, choisit l'agent | Gratuit, déterministe | Rigide ; « Explique ce dépôt » ne contient aucun mot-clé |
| B. Le chef appelle ses collègues comme des outils | `confier()` exécute le collègue **dans** l'appel d'outil, puis le chef reformule | Le chef peut enchaîner plusieurs collègues | Deux fois plus de tokens ; une demande d'accord (`write_file`) au milieu d'un appel d'outil est très difficile à mettre en pause et à reprendre |
| C. **Passage de relais** (*handoff*) | Le chef choisit, puis s'efface : le collègue reprend la conversation et répond | Simple ; les pauses pour accord marchent comme avant ; le collègue répond « directement » | Un seul collègue par demande (une chaîne de postes viendra en phase E, case 141) |

**Choix : C.** C'est aussi ce que l'utilisateur a demandé : le collègue répond directement. Et c'est le même mécanisme que la revue (Analyste puis Rédacteur, leçon 27). On l'étend simplement à un choix fait par un modèle plutôt que par le code.

## 3. Le code expliqué

### Côté API

`AgentKeys.Leader = "chef"` (Contracts) : la clé que le client envoie. Le chef **n'est pas un poste**. Il n'est ni dans `AgentRoles.All`, ni dans `GET /agents`, ni assis à un bureau (le module Team s'appuie sur `RoleKeysAsync` pour créer les sièges). `MissionsModule.IsProposedAsync` l'accepte à part.

`AgentRoles.Leader` a un seul outil, `confier`. `LeaderOf(team)` ajoute à sa consigne la liste de l'équipe (`- analyste (Analyste) : …`). Le modèle ne peut choisir qu'une clé de cette liste. Une clé inventée reçoit « Collègue inconnu » et la liste.

Dans `AgentFrameworkRunner.RunAsync`, quand l'étape en cours est celle du chef :

1. On crée `confier` comme une fonction locale qui **note** le choix (`chosen`, `brief`), sans rien exécuter.
2. Puis `FunctionInvokingChatClient.CurrentContext.Terminate = true` : la boucle d'outils s'arrête là, sans rappeler le modèle du chef. Sans cette ligne, le chef reprendrait la parole pour dire « C'est transmis ! », ce qui coûte un appel au modèle et pollue la réponse.
3. S'il n'a rien confié, sa réponse est la réponse de la mission.
4. Sinon, on note `delegue:<clé>` dans le journal. L'étape devient celle du collègue, avec la demande suivie de « Consigne du chef d'équipe : … ». La suite (outils, accord, réponse en direct, « Répondre ») est exactement celle d'une question posée en direct.

`TeamAsync` décide à qui il peut confier la demande : les postes du code et les agents créés, ceux de l'instance et ceux de qui a lancé la mission. Si le projet a une équipe, seulement ceux qui y ont un bureau.

### Côté client

- `HomeViewModel.SelectedAgent == null` veut dire « le chef ». Le titre devient « Quelle mission pour l'équipe ? », et `LaunchMission` envoie `agent: "chef"`.
- `CrewMember.IsDimmed` efface le chef quand on parle à un autre. `State` donne la bulle : `Busy` (trois points), ou `Waiting` (la cloche). Pour la cloche, seul le **dernier** du circuit d'une revue attend votre accord.
- `Folders` lit le disque local : `Short` remplace le dossier personnel par « ~ », `BranchOf` lit `.git/HEAD`, `Children` liste les sous-dossiers visibles, et `Within` applique la même règle de racine que le serveur. **Le serveur reste juge** : `PUT /projects/{id}/workspace` revérifie tout (ADR 0014). Le client ne fait que proposer.
- La navigation (`Browse`, `Enter`, `Select`, `Up`, `Choose`) ne monte jamais au-dessus d'un dossier autorisé. Au-dessus de la racine, `Up` referme la navigation.
- Le glisser-déposer est géré dans `MainWindow` pour couvrir toute la fenêtre. Un seul élément glissé qui est un dossier : voile, puis `SetFolderAsync`. Des fichiers déposés sur le compositeur sont joints à la mission (leçon 115), et ce gestionnaire passe avant celui de la fenêtre (`e.Handled`).

## 4. Vérifie toi-même

```bash
dotnet test tests/AgentYattus.Api.Tests --filter "FullyQualifiedName~leader"
```

```bash
dotnet test tests/AgentYattus.Client.Core.Tests --filter "FullyQualifiedName~HomeViewModelTests|FullyQualifiedName~FacesTests"
```

- `The_leader_hands_the_request_to_a_colleague_who_answers_directly` : le chef appelle `confier(analyste)`, l'Analyste liste les fichiers et répond. **3 appels au modèle, pas 4** : c'est `Terminate` qui l'assure.
- `The_leader_answers_a_simple_question_himself` : un seul appel au modèle, et sa réponse est le résultat.
- `The_owner_browses_to_a_folder_in_the_app_without_climbing_above_the_allowed_root` : le dossier caché n'apparaît pas, la branche est lue, et `Up` ne sort pas de la racine.

Dans le logiciel, glisse un dossier depuis le Finder sur la fenêtre, puis clique sur Inès : Léon s'efface et le titre change.

## 5. Les limites (assumées)

- **Un seul collègue par demande.** Pour enchaîner « l'Analyste puis le Testeur », il faudra une chaîne de postes (phase E, case 141).
- **Le budget** (`RolesFor`) compte le chef et les postes du code. Un agent créé à qui le chef confierait la demande n'est pas compté avant le départ (`ponytail:` dans le code).
- **L'open space** joue une demande faite au chef au bureau de l'Analyste. Confiée au Rédacteur, la scène le fait parler depuis son bureau ; confiée à un agent créé sans bureau, ses répliques ne s'affichent pas.
- **La pastille de mission** montre le visage du chef, pas celui du collègue qui travaille. Le journal (`delegue:`) le sait. Le visage pourra suivre.
- **Pas de flou sous le voile** de dépôt : Avalonia ne sait pas flouter une partie de la fenêtre. Le voile est plus sombre que dans la maquette.
- **Un serveur distant** : la branche est lue sur cet ordinateur. Si le dossier du projet n'existe pas ici, la pastille affiche « branche » sans nom.

## 6. Quiz

1. Pourquoi le chef n'est-il pas dans `AgentRoles.All` ?
2. Que se passerait-il sans `Terminate = true` ?
3. Pourquoi le passage de relais plutôt que « le chef appelle l'Analyste comme un outil » ?
4. Le client vérifie déjà que le dossier est sous une racine autorisée. Pourquoi le serveur le vérifie-t-il encore ?

<details>
<summary>Réponses</summary>

1. `All` alimente la liste des postes, celle des agents et les sièges d'équipe. Le chef aurait eu un bureau à pourvoir et une fiche dans « Agents ». Ce n'est pas un poste, c'est un rôle de répartition.
2. La boucle rappellerait le modèle du chef après l'outil. On paierait un appel de plus, et le chef ajouterait sa phrase avant celle du collègue.
3. Un accord humain (`write_file`) met la mission en pause et enregistre la conversation de **l'agent qui attend**. Dans un appel d'outil imbriqué, il faudrait sauver deux conversations et reprendre au milieu d'un outil. Avec le relais, c'est la conversation du collègue, comme avant.
4. Le client est sur la machine de l'utilisateur : on ne lui fait jamais confiance. Une requête `PUT` peut venir d'ailleurs que de notre interface (ADR 0014).

</details>
