# Leçon 119 — La réponse en direct

> **Commit** : `feat(agents): la réponse arrive en direct, par SignalR`
> **Phase** : B
> **Fichiers** : `Contracts/Missions.cs`, `Api/Modules/Agents/{AgentFrameworkRunner,AgentsModule}.cs`, `Api/Modules/Missions/MissionWorker.cs`, `tests/{ScriptedChatClient,ApiFactory,ConversationTests}.cs`

## 1. Le problème

Une réponse de modèle met souvent 10 à 30 secondes à s'écrire. Jusqu'ici, on ne voyait rien avant la fin. Une interface qui reste figée donne l'impression que rien ne se passe.

## 2. Le streaming

Les fournisseurs savent rendre la réponse **par morceaux**, au fur et à mesure (*server-sent events*). Agent Framework l'expose avec `RunStreamingAsync`. Chaque `AgentResponseUpdate` porte un morceau de texte, ou un appel d'outil, ou des tokens comptés.

```csharp
await foreach (var update in agent.RunStreamingAsync(history, cancellationToken: cancellationToken))
{
    updates.Add(update);
    if (onText is not null && update.Text is { Length: > 0 } text)
        await onText(text);
}
var response = updates.ToAgentResponse();
```

`ToAgentResponse` reconstitue **la même réponse complète** qu'avant : appels d'outils, demandes d'accord, tokens. Tout le reste du runner (journal, coût, pauses) ne change pas. Les 146 tests existants le confirment.

## 3. Du runner au logiciel

- Le module Agents ne connaît pas SignalR : il reçoit une simple fonction `onText`.
- `MissionRunner` l'implémente. Il accumule les morceaux et les envoie **toutes les 150 ms au plus** (`MissionText`), aux seules personnes qui peuvent lire le projet, comme les statuts.
- Envoyer chaque morceau d'un ou deux mots ferait des centaines de messages par réponse. 150 ms reste fluide à l'œil.

Seule la **réponse finale** est diffusée. Les constats intermédiaires de l'Analyste, dans une revue, restent internes.

## 4. Vérifie toi-même

`dotnet test --filter The_answer_reaches_the_readers` : un client SignalR écoute, et les morceaux mis bout à bout font la réponse.

Le faux modèle des tests découpe maintenant ses réponses comme un vrai (`ToChatResponseUpdates`).

## 5. Les limites

- Le mode Gratuit (`FallbackChatClient`) rend sa réponse d'un bloc : il doit pouvoir basculer avant le premier morceau. Le texte arrive donc en une fois.
- Un morceau perdu (coupure réseau) n'est pas renvoyé. Le fil se recharge à la fin, depuis l'API.

## 6. Quiz

1. Pourquoi regrouper les morceaux plutôt que de tous les envoyer ?

<details>
<summary>Réponse</summary>

1. Chaque morceau est un message SignalR vers chaque lecteur. Les regrouper divise le trafic par dix ou plus, sans différence visible.

</details>
