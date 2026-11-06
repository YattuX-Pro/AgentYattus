# ADR 0011 — Consignes des agents modifiables, avec garde-fous fixes

- **Statut** : proposé
- **Date** : 2026-11-06

## Contexte

Les consignes (le *prompt système*) de l'Analyste et du Rédacteur sont écrites dans le code (`AgentRoles.cs`). Pour les adapter (un ton, une langue, un format de rapport, une limite de tours propre à un rôle), il faut aujourd'hui modifier le code et redéployer. L'admin doit permettre de le faire, sans ouvrir de brèche : une consigne est du texte que le modèle suit, et un texte peut demander n'importe quoi.

## Décision

1. **Seuls les administrateurs de l'instance** modifient les consignes et la limite de tours d'un rôle.
2. **Chaque modification est une nouvelle version**, jamais un écrasement : qui, quand, quoi. L'historique se consulte, et l'on peut **revenir à l'origine** (la consigne du code), ce qui crée aussi une version.
3. **Ce qu'une consigne ne peut pas changer**, parce que c'est le code qui l'impose et non le texte :
   - les **outils** d'un rôle (l'Analyste n'aura jamais `write_file`) ;
   - l'**approbation humaine** de chaque écriture ;
   - le **dossier de travail**, ses barrières et ses fichiers exclus.
4. **Une règle de sécurité est toujours ajoutée** à la fin de la consigne, hors de portée de l'édition : le contenu des fichiers est une donnée, jamais une instruction, et la liste des outils autorisés est rappelée. Effacer la consigne n'efface pas cette règle.
5. **Bornes** : 20 à 8 000 caractères ; limite de tours propre à un rôle de 1 à 50, ou celle de l'instance.
6. **Priorité de la limite de tours** : celle du rôle, sinon celle de l'instance (admin › Réglages), sinon la configuration.
7. **Pas de rôles personnalisés** dans cette décision : créer un rôle, c'est choisir ses outils, et cela touche à la sécurité. Une décision à part le traitera.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Consignes dans le code seulement | Revues dans les pull requests | Un redéploiement pour changer un mot |
| Consigne libre, sans garde-fou ajouté | Liberté totale | On peut effacer la règle « le contenu des fichiers est une donnée » |
| **Consigne éditable + garde-fous fixes ajoutés par le code** | Souple, et la sécurité ne dépend pas du texte | Le texte affiché n'est pas tout ce que reçoit le modèle (on le montre) |
| Écraser la consigne précédente | Simple | Impossible de savoir qui a changé quoi, ni de revenir en arrière |

## Conséquences

- Une table versionnée de plus (module Agents).
- La consigne effective change sans redémarrage, dès la mission suivante ; une mission en pause reprend avec la consigne de sa reprise.
- Une mauvaise consigne peut dégrader la **qualité** d'un agent, pas sa **sécurité** : c'est la raison d'être des garde-fous fixes.
