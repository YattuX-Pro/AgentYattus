# ADR 0018 — Les clés de modèle personnelles

- **Statut** : proposé
- **Date** : 2026-11-08

## Contexte

Une clé d'API (Anthropic, OpenAI, Mistral…) appartenait à toute l'instance : seul un administrateur (`llm-admin`) pouvait en connecter une, et toutes les missions la payaient (ADR 0006, ADR 0007). L'ADR 0009 avait reporté les clés personnelles. Dans une équipe, chacun veut pouvoir utiliser son propre compte chez un fournisseur, sans le donner aux autres ni à l'administrateur, et sans que cela pèse sur le budget commun.

## Décision

1. **Une connexion a un propriétaire** (`OwnerId`) : null pour une connexion de l'instance, l'identifiant de l'utilisateur pour une clé personnelle. La clé reste chiffrée par Data Protection et n'est jamais renvoyée (4 derniers caractères).
2. **Tout utilisateur connecté gère les siennes**, routes `/llm/mine` : les lister, en connecter une, la révoquer (la clé chiffrée est effacée), choisir son modèle par défaut parmi elles. Une clé d'un autre n'existe pas pour lui (404), administrateur compris.
3. **Fournisseurs en ligne seulement** (`LlmConnectionKind.ApiKey`), à l'adresse imposée par le code. Un serveur local ou une adresse au choix ferait appeler par le serveur une adresse que l'utilisateur choisit : c'est une décision d'administrateur.
4. **Le modèle d'une mission** est cherché dans cet ordre, pour la personne qui l'a lancée :
   1. le modèle de l'agent s'il passe par une de ses clés (un agent personnel) ;
   2. son modèle par défaut personnel ;
   3. le mode Gratuit de l'instance ;
   4. le modèle de l'agent sur une connexion de l'instance, puis le modèle par défaut de l'instance ;
   5. la clé de la configuration (développement).
   La clé personnelle d'un autre ne sert **jamais**, même dans un projet partagé.
5. **Les réglages de l'instance n'acceptent jamais une clé personnelle** : ni le modèle par défaut, ni la chaîne Gratuite (même remplie automatiquement), ni le modèle d'un agent de l'instance. Une clé personnelle ne sert au modèle d'un agent que si l'agent appartient à son propriétaire.
6. **Le budget** : un appel par une clé personnelle n'est pas payé par l'instance. La mission garde sa part (`PersonalCostMicros`) ; le budget du mois compte le coût total moins cette part, et le blocage au plafond ignore une mission qui passe par une clé personnelle. Le journal note `cle:personnelle` ou `cle:instance` à chaque appel.
7. **Dans le bureau**, l'entrée « Modèles et clés » du menu du portrait ouvre, sur un serveur, l'écran **Mes clés** ; en mode local, l'écran de l'instance (ADR 0014).

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Garder les seules clés de l'instance | Rien à faire | Chacun dépend de l'administrateur et partage une facture |
| Une clé par projet | Partagée par une équipe | Qui la voit, qui la paie ? À décider à part ; pas demandé |
| **Une clé par utilisateur, pour ses missions** | Simple à expliquer, cloisonnée | Une mission dans un projet partagé coûte à qui la lance, pas au projet |

## Conséquences

- (+) Personne ne peut utiliser la clé d'un autre ; l'administrateur ne la voit pas.
- (+) Le budget de l'instance mesure ce que paie l'instance.
- (−) Une clé personnelle est déchiffrée **sur le serveur** pour appeler le fournisseur : un administrateur du serveur (qui a la base et le trousseau) pourrait la lire. Ce n'est pas un coffre de bout en bout.
- (−) Pas de serveur local personnel (Ollama sur son propre poste, vu du serveur) : l'adresse serait celle du serveur.
- (−) Pas encore de budget par utilisateur : le coût personnel est compté, pas plafonné.
