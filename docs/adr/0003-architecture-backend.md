# ADR 0003 — Backend : monolithe modulaire, file en mémoire, temps réel par SignalR

- **Statut** : proposé
- **Date** : 2026-11-05

## Contexte

Le backend reçoit des missions, les fait exécuter par des agents, garde une trace de chaque étape et prévient les clients (desktop, puis mobile) en direct.

Contraintes :

- une seule personne développe, sur 4 à 5 mois ;
- le projet doit tourner sur un portable, avec Docker Compose pour PostgreSQL ;
- une mission dure de quelques secondes à plusieurs minutes : bien plus longtemps qu'une requête HTTP ne doit attendre ;
- le découpage doit rester assez net pour qu'un module puisse évoluer, voire sortir du processus, sans tout réécrire.

Cet ADR décrit ce que le squelette (`src/AgentYattus.Api`) a mis en place, et pourquoi.

## Décision

1. **Un seul processus ASP.NET Core découpé en modules** : `Missions`, `Agents`, `Audit`, chacun dans `Modules/<Nom>`.
   - Chaque module s'enregistre lui-même (`AddMissionsModule`, `MapMissionsEndpoints`…).
   - Un module n'utilise que les **types publics** d'un autre module : `IAgentRunner` pour les agents, `AuditLog` pour l'audit. Tout le reste est `internal`.
   - Comme les modules partagent un assembly, `internal` ne les isole pas entre eux. La règle est donc vérifiée par un test (`ArchitectureTests`).
2. **Minimal APIs** pour les points d'entrée HTTP, avec des réponses typées (`TypedResults`).
3. **PostgreSQL avec EF Core : un `DbContext` et un schéma par module** (`missions`, `audit`). Aucune table n'est partagée entre modules.
4. **Exécution asynchrone** :
   - `POST /missions` enregistre la mission (`Pending`), pose son identifiant dans une file en mémoire (`System.Threading.Channels`) et répond **202 Accepted** avec l'adresse de la mission ;
   - un service d'arrière-plan (`MissionWorker`, un `BackgroundService`) lit la file et exécute **une mission à la fois**, chacune dans sa propre portée d'injection de dépendances ;
   - l'échec d'une mission la marque `Failed` sans arrêter le service.
5. **Temps réel par SignalR** : le hub `/hubs/missions` diffuse un `MissionEventDto` à chaque changement de statut. Le hub ne va que du serveur vers le client.
6. **Un projet `Contracts`** contient les objets échangés (DTO, nom du hub et des événements). Il est partagé par l'API et les clients.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Microservices (un service par module) | Déploiement et montée en charge indépendants | Réseau, déploiement, traçage distribué et cohérence des données : beaucoup trop pour une personne et un MVP |
| Clean Architecture en 4 projets | Séparation stricte des couches | Beaucoup de fichiers pour peu de logique métier à ce stade |
| **Monolithe modulaire** | Un seul déploiement, frontières nettes et vérifiées par un test | Il faut tenir la discipline des frontières, d'où le test |
| Courtier de messages (RabbitMQ, Wolverine) au lieu d'une file en mémoire | Les missions survivent à un redémarrage | Une infrastructure de plus à faire tourner et à apprendre |
| Interrogation périodique (*polling*) au lieu de SignalR | Plus simple côté serveur | Du retard à l'écran, et des requêtes inutiles |
| Une base par module au lieu d'un schéma | Isolation maximale | Plusieurs bases à gérer en local, pour un gain faible à ce stade |

## Conséquences

- (+) Un seul processus à lancer, à déboguer et à déployer.
- (+) Chaque module a ses tables et une surface publique réduite : on pourra le sortir du processus plus tard si besoin.
- (+) Le client reçoit une réponse immédiate (202), puis les mises à jour arrivent en direct.
- (−) **La file est en mémoire** : au redémarrage, une mission en attente est perdue, et une mission en cours reste bloquée à `Running`. La reprise après redémarrage est prévue après le MVP.
- (−) **Une mission à la fois.** Avant l'étape « Approbations », une mission en pause devra libérer le service au lieu de l'occuper (phase 2).
- (−) Les tables sont créées au démarrage, sans migrations (`EnsureCreated`, par module). On passera aux migrations EF Core avant le premier changement de schéma.
- (−) Chaque événement est diffusé à tous les clients connectés. Il faudra des groupes par mission quand il y aura plusieurs utilisateurs.
- (−) Le test d'architecture lit les signatures (constructeurs, champs, paramètres), pas le corps des méthodes. Un `new` caché dans une méthode lui échapperait.

## Sources

- Minimal APIs : <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/overview>
- Services d'arrière-plan : <https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services>
- `System.Threading.Channels` : <https://learn.microsoft.com/dotnet/core/extensions/channels>
- SignalR : <https://learn.microsoft.com/aspnet/core/signalr/introduction>
- EF Core : <https://learn.microsoft.com/ef/core>
