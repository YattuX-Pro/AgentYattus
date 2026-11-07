# Leçon 121 — Les missions reprennent après un redémarrage

> **Commit** : `feat(missions): les missions interrompues reprennent après un redémarrage`
> **Phase** : B
> **Fichiers** : `Api/Modules/Missions/MissionWorker.cs`, `tests/{ApiFactory,RestartTests}.cs`

## 1. Le problème

La file des missions vit **en mémoire** (un `Channel`). Si l'API s'arrête (mise à jour, plantage, fermeture du logiciel en mode local), deux choses se perdent :

- une mission **en file** n'est plus dans aucune file : elle reste « En attente » pour toujours ;
- une mission **en cours** reste « En cours » pour toujours.

## 2. La décision

La base est la vérité, la file n'est qu'un raccourci. **Au démarrage**, le worker relit la base et remet en file :

| Statut trouvé | Ce qu'on fait |
|---|---|
| `Pending` | Remise en file |
| `Running` | Remise en `Pending`, « reprise » dans le journal, puis en file |
| `AwaitingApproval` **avec** une décision | Remise en file : la décision avait été prise juste avant l'arrêt |
| `AwaitingApproval` sans décision | Rien : elle attend toujours un humain |

## 3. Reprendre au bon endroit

Il y avait un piège : l'exécution effaçait l'état de reprise (conversation, décision, réponse en attente) **en commençant**. Coupée au milieu, elle aurait repris de zéro, sans la décision de l'humain.

L'état reste donc en base **jusqu'à la fin de l'exécution** :

- elle va au bout : on l'efface, puis on enregistre le nouvel état (pause, ou conversation terminée) ;
- elle échoue ou est arrêtée : on l'efface, plus rien à reprendre ;
- elle est coupée par un arrêt : l'état est intact, la reprise repart exactement d'ici.

Et le choix « décision » ne dépend plus du statut (`Running` après un arrêt) : il suffit d'une décision, d'une demande en attente et d'une conversation.

## 4. Attendre que l'application ait démarré

Le worker relit la base après `ApplicationStarted`. Les services démarrent dans l'ordre où ils ont été enregistrés. Sur une base neuve, le worker pourrait sinon lire avant que les tables existent.

## 5. Vérifie toi-même

`dotnet test --filter RestartTests` : une première API laisse une mission en file, une « en cours » et une en pause. Une seconde API sur la même base termine les deux premières, laisse la troisième en pause, et note « reprise ».

## 6. Les limites

- Une mission coupée en plein travail **recommence son tour depuis le dernier état enregistré** : les appels au modèle de ce tour sont refaits, et payés. Les écritures, elles, ne sont jamais refaites sans accord humain.
- Une seule instance de l'API : plusieurs instances remettraient la même mission en file deux fois. Une file en base (ou un verrou) viendrait avec le passage à plusieurs serveurs.

## 7. Quiz

1. Pourquoi ne pas effacer la décision au moment où l'exécution commence ?

<details>
<summary>Réponse</summary>

1. Si l'API s'arrête pendant l'exécution, la décision serait perdue : la mission reprendrait sans elle, et la demande d'accord ne réapparaîtrait pas.

</details>
