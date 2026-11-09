# ADR 0016 — La mission Développer : copie isolée, contrôles lancés par le code, relecture distincte

- **Statut** : proposé
- **Date** : 2026-11-07

## Contexte

Les outils d'un développeur existaient (`search`, `edit_file`, `run_command`, git, mode branche), mais pas le parcours : comprendre la demande, explorer le dépôt, modifier, vérifier, relire, présenter. Deux défauts empêchaient un travail fiable :

1. **Chaque écriture et chaque commande demandaient un accord**, même dans la copie de travail d'une mission sur branche. Un agent ne pouvait pas enchaîner modifier, compiler, corriger : l'humain approuvait des dizaines de pas sans enjeu.
2. **Rien ne distinguait une vérification réelle d'une affirmation.** Le modèle écrivait « les tests passent » ; personne n'avait lancé les tests, ou personne ne savait si un échec était nouveau.

## Décision

1. **Un type de mission `Develop`**, toujours sur une branche à part (`OnBranch` forcé). Sans dépôt git avec un commit, la mission échoue : on ne touche jamais au dossier de l'humain.
2. **Dans la copie de travail d'une mission** (créée par le code sous `Agents:WorktreesFolder`), `write_file`, `edit_file`, `write_document` et `run_command` **ne demandent plus d'accord** : rien n'atteint les fichiers du projet. `git_commit` garde l'accord : c'est la livraison. Hors copie de travail, rien ne change.
3. **Les étapes sont tenues par le code** (`AgentFrameworkRunner.DevelopAsync`), pas par la consigne :
   1. le **profil du dépôt** (`explore_repository`) : langages, projets, consignes du dépôt (AGENTS.md, CLAUDE.md, README…), commandes de contrôle déduites des fichiers racine ;
   2. les **contrôles au départ**, lancés par le code dans le bac à sable (ADR 0015), avant toute modification ;
   3. le **Développeur** fixe des critères d'acceptation, modifie, vérifie, et rend un rapport à sections fixes ;
   4. les **contrôles après**, relancés par le code, comparés à ceux du départ : réussi, corrigé, **régression**, **échec déjà présent**, ou **non vérifié** (bac à sable, outil ou réseau indisponible) ;
   5. le **Relecteur**, un autre agent en lecture seule, juge le diff, le rapport et les contrôles : « VERDICT : ACCEPTÉ » ou « À CORRIGER » ;
   6. **une correction au plus** (deux tours de développement), puis le résultat.
4. **Le résultat est assemblé par le code** : le rapport de l'agent (une affirmation), puis les faits (contrôles exécutés, relecture, compétences et sources, branche, fichiers modifiés, diff borné). Un contrôle qui n'a pas tourné n'est jamais présenté comme réussi.
5. **`.git` est protégé** : les outils d'écriture le refusent, et le bac à sable le monte en lecture seule, qu'il soit un dossier ou le fichier d'une copie de travail.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Garder l'accord à chaque écriture | Rien à changer | Inutilisable pour corriger un test qui échoue : dix accords par minute |
| Un seul agent, à qui la consigne demande de vérifier | Simple | « Les tests passent » reste une affirmation ; aucune relecture indépendante |
| **Copie isolée sans accord, contrôles par le code, Relecteur distinct** | Les faits viennent du code ; l'humain décide à la livraison | Docker requis pour vérifier ; deux agents, donc plus de tokens |

## Conséquences

- (+) L'humain voit ce qui a réellement tourné, et sait si un échec est nouveau.
- (+) La relecture ne dépend pas de l'agent qui a écrit.
- (−) Sans Docker, aucun contrôle ne tourne : le résultat le dit (« Aucun contrôle n'a pu tourner »).
- (−) Sans réseau dans le bac à sable, une restauration de paquets échoue : le contrôle est « non vérifié », pas « échec ».
- (−) Les contrôles sont déduits des fichiers **à la racine** du dépôt ; un dépôt rangé autrement n'en a pas (ponytail dans `RepositoryProfile`).
- (−) Une commande de la copie de travail peut tout y modifier : c'est voulu, le diff le montre, et rien n'est commité sans accord.
- (−) Une reprise après redémarrage repart du début du parcours : les contrôles « au départ » ne sont alors plus mesurés (la copie est déjà modifiée), et le résultat le dit.
