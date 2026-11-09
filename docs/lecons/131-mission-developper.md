# Leçon 131 — La mission Développer : modifier à part, vérifier pour de vrai, relire

> **Commit** : `feat(missions): Développer, une branche à part, des contrôles lancés par le code et une relecture`
> **Phase** : parcours de développement (P0) · **ADR** : 0016
> **Fichiers** : `Contracts/Missions.cs`, `Api/Modules/Agents/{AgentFrameworkRunner,AgentRoles,DevelopChecks,GitBranches,WorkspaceTools,Sandbox}.cs`, `Api/Modules/Missions/{MissionsModule,MissionWorker}.cs`, `Client.Core/{HomeViewModel,MissionItem,OfficeViewModel,Scene}.cs`, `Desktop/Views/HomeView.axaml`, `tests/{DevelopMissionTests,DevelopChecksTests,BranchModeTests}.cs`

## 1. Ce qu'on construit

À l'accueil, une pilule **Développer**. La mission suit six étapes, tenues par le code :

1. le **profil du dépôt** (leçon 130) ;
2. les **contrôles au départ**, dans le bac à sable, avant toute modification ;
3. le **Développeur** : critères d'acceptation, modifications, vérifications, rapport ;
4. les **contrôles après**, comparés au départ ;
5. le **Relecteur** juge le diff : `VERDICT : ACCEPTÉ` ou `À CORRIGER` ;
6. **une correction au plus**, puis le résultat.

Le résultat se lit ainsi :

| Contrôle | Commande | Avant | Après | Verdict |
|---|---|---|---|---|
| tester | `dotnet test App.slnx` | échec : code 1 | réussi (12 s) | corrigé |

## 2. Le problème

« Les tests passent », écrit par un modèle, n'est pas un fait. Et un échec n'est pas forcément une régression : il était peut-être déjà là, ou Docker n'a pas pu télécharger les paquets.

## 3. Les options

Voir l'ADR 0016. Choix : **copie de travail isolée sans accord à chaque pas, contrôles par le code, Relecteur distinct, une boucle de correction bornée.**

## 4. Le code expliqué

- **Isolée** : `AgentFrameworkRunner.IsMissionWorktree` reconnaît une copie de travail **créée par le code** sous `Agents:WorktreesFolder`. Dans ce cas, `WorkspaceTools(isolated: true)` ne met plus `ApprovalRequiredAIFunction` sur `write_file`, `edit_file`, `run_command` et `write_document`. `git_commit` garde l'accord.
- **`.git` protégé** : `ResolveForWriting` refuse tout chemin qui commence par `.git`. Dans une copie de travail, `.git` est un **fichier** (`gitdir: …`) qui désigne le dépôt. Le réécrire ferait travailler git, sur la machine, dans un autre dépôt. Le bac à sable le monte en lecture seule, qu'il soit dossier ou fichier.
- **`DevelopChecks.Classify`** lit un résultat de commande :
  - code 0 : passé ;
  - Docker absent, ou code 125 à 127 (image ou commande absente) : **indisponible** ;
  - une sortie qui réclame le réseau (`NU1301`, `ENOTFOUND`…) : **indisponible** ;
  - sinon : échec.
- **`DevelopChecks.Compare`** compare avant et après : régression, échec déjà présent, corrigé, réussi, non vérifié.
- **`DevelopAsync`** retire `git_commit` au Développeur pendant les étapes : un accord en plein parcours le couperait. Il désactive aussi l'apprentissage (`propose_skill`), qui mettrait la mission en pause.
- **`GitBranches.Changes`** indexe tout **dans la copie de travail de la mission** (jamais dans le dépôt de l'humain), puis compare au commit de départ : fichiers nouveaux compris, avec `+n −m`, et un diff borné à 20 000 caractères.
- **`Compose`** assemble le résultat : le rapport de l'agent (une affirmation), puis les faits.
- Au passage, deux corrections du mode branche :
  - le module Missions ne dépend plus d'un type interne du module Agents (`IMissionWorktrees`) : le test d'architecture repasse ;
  - `docker kill` est borné à 10 secondes : un démon Docker bloqué ne bloque plus une mission.

## 5. Vérifie toi-même

```bash
dotnet test tests/AgentYattus.Api.Tests --filter-class "*DevelopMissionTests"
```

```bash
dotnet test tests/AgentYattus.Api.Tests --filter-class "*DevelopChecksTests"
```

- le Développeur modifie sa copie sans accord ; le dossier du projet n'a pas changé ; quatre commandes (deux avant, deux après) ; « corrigé » ;
- une relecture « À CORRIGER » renvoie le Développeur **une** fois, pas deux ; une régression est nommée ;
- sans Docker : « Aucun contrôle n'a pu tourner », jamais « réussi » ;
- sans dépôt git : la mission échoue sans appeler le modèle.

## 6. Les limites

- **Pas encore de reprise étape par étape** : coupée par un redémarrage, la mission repart du début. Les contrôles « avant » sont alors « non mesurés », et le résultat le dit.
- **Le commit d'après** (en répondant à l'agent) prend tout ce qui est dans la copie de travail au moment du commit. L'accord n'est pas encore lié à l'empreinte du diff montré.
- **Les copies de travail ne sont pas nettoyées.** Il faudra un nettoyage contrôlé (après fusion, ou après N jours).
- **Le bac à sable sans réseau** : une restauration de paquets échoue (« non vérifié »).
- **L'open space** joue le Développeur au bureau de l'analyste ; le Relecteur n'a pas encore de bureau.
- **Rien n'a été vérifié visuellement** dans le logiciel : la pilule et le résultat suivent les styles existants.

## 7. Quiz

1. Pourquoi lancer les contrôles **avant** de modifier ?
2. Pourquoi le Relecteur ne peut-il rien écrire ?
3. Pourquoi deux tours au plus ?

<details><summary>Réponses</summary>

1. Pour distinguer une régression (passait, échoue) d'un échec déjà présent (échouait déjà).
2. S'il corrigeait lui-même, on n'aurait plus de relecture indépendante : il jugerait son propre travail.
3. Chaque tour coûte des tokens et du temps ; au-delà d'une correction, l'humain doit reprendre la main (il peut répondre à l'agent).

</details>
