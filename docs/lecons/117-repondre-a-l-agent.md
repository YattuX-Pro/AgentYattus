# Leçon 117 — Répondre à l'agent

> **Commit** : `feat(missions): répondre à l'agent, dans la même conversation`
> **Phase** : B, « Une vraie conversation »
> **Fichiers** : `Contracts/Missions.cs`, `Api/Modules/Missions/{Mission,MissionWorker,MissionsModule}.cs`, `Api/Modules/Agents/{AgentFrameworkRunner,AgentsModule}.cs`, migration `Conversations`, `tests/ConversationTests.cs`

## 1. Le problème

Une mission était un seul aller-retour. Pour dire « et combien de routes ? », il fallait une nouvelle mission, qui repartait de zéro : relire les fichiers, payer à nouveau.

## 2. La décision

La conversation est déjà ce qu'on garde pendant une pause (leçon 20). On la garde maintenant **aussi à la fin**, et une réponse de l'humain reprend la mission exactement comme une décision :

| | Pause (accord) | Réponse (nouveau) |
|---|---|---|
| Ce qui relance | `POST /missions/{id}/decision` | `POST /missions/{id}/messages` |
| Ce qui est ajouté à la conversation | La décision sur la demande | Le message de l'humain |
| Statut avant | `AwaitingApproval` | `Completed` |

Chaque tour (réponse de l'agent, message de l'humain) entre dans une table `Turns`. `GET /missions/{id}/turns` rend le fil. `Result` reste la dernière réponse.

## 3. Le code expliqué

Dans le runner, une seule ligne choisit ce qu'on ajoute :

```csharp
stage.History.Add(resume.FollowUp is { } followUp
    ? new ChatMessage(ChatRole.User, followUp)
    : new ChatMessage(ChatRole.User, AnswerPendingRequests(stage.History, resume.Approved)));
```

Côté API, la relance est **une seule requête** qui vérifie et écrit, comme la décision :

```csharp
.Where(m => m.Id == id && m.Status == MissionStatus.Completed && m.Conversation != null)
.ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MissionStatus.Pending).SetProperty(m => m.FollowUp, text))
```

Deux réponses simultanées ne relancent pas deux fois.

## 4. Vérifie toi-même

`dotnet test --filter ConversationTests` :

- le deuxième appel au modèle contient le but, la première réponse et la question ;
- le fil a trois tours ;
- une mission sans conversation répond 409, un message vide 400, quelqu'un sans accès 404.

## 5. Les limites

- **La conversation grossit.** Au-delà d'un million de caractères (environ 250 000 tokens), elle n'est pas gardée : on lance une nouvelle mission.
- **Elle contient ce que l'agent a lu.** Comme `Result`, elle reste en base, jamais dans les journaux.
- **Pas de réponse à une mission échouée ou arrêtée** : pour elles, c'est « Relancer ».

## 6. Quiz

1. Pourquoi réutiliser le mécanisme de la pause plutôt qu'en écrire un nouveau ?

<details>
<summary>Réponse</summary>

1. La reprise d'une conversation enregistrée existe déjà et elle est testée. Seul ce qu'on ajoute à la fin change : moins de code, et les mêmes garanties.

</details>
