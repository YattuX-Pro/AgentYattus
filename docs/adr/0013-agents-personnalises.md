# ADR 0013 — Ajouter des agents depuis l'admin

- **Statut** : proposé
- **Date** : 2026-11-07

## Contexte

L'Analyste et le Rédacteur sont écrits dans le code. L'ADR 0011 rend leur consigne modifiable et repousse les rôles sur mesure, parce que créer un rôle, c'est choisir ses outils, ce qui touche à la sécurité. On veut maintenant ajouter des agents depuis l'admin (un « Testeur », un « Documentaliste »…), sans redéployer.

## Décision

1. **Un administrateur de l'instance crée un agent** : un nom, une phrase de présentation, une consigne et des **outils pris dans la liste existante** (`list_files`, `read_file`, `write_file`). Un agent sans outil est refusé.
2. **L'écriture reste soumise à l'accord humain**, que l'agent ait été créé dans l'admin ou non : c'est `WorkspaceTools` qui impose l'approbation de `write_file`, pas le rôle. Les garde-fous (le contenu des fichiers est une donnée, la liste réelle des outils) sont toujours ajoutés à la consigne par le code (ADR 0011).
3. **On choisit l'agent au lancement d'une mission « Question »** : il la fait seul. La « Revue » reste le circuit Analyste puis Rédacteur. Pas de chaîne d'agents ni de chef qui délègue dans cette décision.
4. **Un agent créé se comporte comme les autres** : consigne versionnée (ADR 0011), modèle propre (ADR 0007), statistiques, siège dans l'équipe d'un projet et portrait (ADR 0012).
5. **L'Analyste et le Rédacteur ne se suppriment pas.** Un agent créé se **retire** (il n'est plus proposé) ; ses missions passées gardent son nom.
6. **L'identifiant** d'un agent (`testeur`) est tiré de son nom et ne change plus : le journal, les statistiques et les sièges s'y rattachent.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| **Agent choisi au lancement** | Simple, prévisible, coût maîtrisé | Un seul agent par mission « Question » |
| Équipe en chaîne configurable | Puissant | Missions plus longues et plus chères, écran de composition à concevoir |
| Chef qui délègue (agent qui appelle des agents) | Autonome | Imprévisible, coûteux, difficile à tester |
| Outils libres (y compris de nouveaux) | Souple | Un outil est du code : il doit passer par une revue, pas par un formulaire |

## Conséquences

- Une table de plus dans le module Agents (`CustomRoles`) et une colonne `Agent` sur les missions.
- Les autres modules ne lisent plus une liste figée de rôles mais `IAgentCatalog`.
- Une mission en pause dont l'agent a été retiré peut encore reprendre : la décision humaine reste possible jusqu'au bout.
