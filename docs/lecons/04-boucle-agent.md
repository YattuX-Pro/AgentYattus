# Leçon 04 — La boucle d'agent, écrite à la main

> **Commit** : `feat(spike): boucle d'agent écrite à la main`
> **Phase** : 0, socle et boucle d'agent
> **Fichiers** : `spikes/AgentLoop/AgentLoop.cs`, `spikes/AgentLoop.Tests/ScriptedChatClient.cs`, `spikes/AgentLoop.Tests/AgentLoopTests.cs`

C'est **la leçon la plus importante du cours**. Tout le reste d'Agent Yattus (frameworks, approbations, orchestration de plusieurs agents) est construit au-dessus de ce mécanisme.

## 1. Ce qu'on construit

Le **moteur** d'un agent : une boucle qui fait dialoguer le modèle et nos outils, jusqu'à ce que la mission soit terminée.

> **Une image** : un chef (le modèle) et un commis (notre code) en cuisine. Le chef ne touche à rien : il dit « apporte-moi les tomates ». Le commis va les chercher et les pose sur le plan de travail. Le chef regarde, puis demande autre chose, ou annonce « le plat est prêt ». Le commis, c'est la boucle.

## 2. Le mécanisme, pas à pas

```
          ┌───────────────────────────────────────────────────────┐
          │ historique : [ but de l'utilisateur ]                  │
          └───────────────────────────────────────────────────────┘
                              │
                 ┌────────────▼────────────┐
         ┌──────►│  1. appeler le modèle   │  (historique + liste des outils)
         │       └────────────┬────────────┘
         │                    │ réponse
         │       ┌────────────▼────────────┐
         │       │ 2. demande-t-il un outil ? ──── non ──► FIN : renvoyer son texte
         │       └────────────┬────────────┘
         │                    │ oui
         │       ┌────────────▼────────────┐
         │       │ 3. exécuter l'outil      │
         │       │ 4. ajouter le résultat   │
         │       │    à l'historique        │
         │       └────────────┬────────────┘
         └────────────────────┘   (sauf si la limite de tours est atteinte)
```

Un point essentiel : **le modèle n'a pas de mémoire**. À chaque appel, on lui renvoie **tout l'historique** : le but, ses demandes précédentes, les résultats des outils. C'est cet historique qui lui permet de « se souvenir ».

## 3. Le code expliqué

### 3.1 Démarrer

```csharp
List<ChatMessage> history = [new(ChatRole.User, goal)];
var options = new ChatOptions { Tools = [.. tools] };
```

- L'historique commence avec **un seul message** : le but, avec le rôle `User`.
- `options.Tools` : la liste des outils que le modèle a le droit de demander.

### 3.2 Appeler le modèle, garder sa réponse

```csharp
var response = await chatClient.GetResponseAsync(history, options, cancellationToken);
history.AddRange(response.Messages);
totalTokens += response.Usage?.TotalTokenCount ?? 0;
```

- On ajoute la réponse **telle quelle** à l'historique. Si on la modifiait, le modèle ne reconnaîtrait plus ses propres demandes au tour suivant.
- On additionne les **tokens** : c'est ce qu'on paie. Un token correspond à peu près à un morceau de mot.

### 3.3 Le modèle a-t-il fini ?

```csharp
var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
if (calls.Count == 0)
    return new AgentRunResult(AgentRunStatus.Completed, response.Text, iteration, history, totalTokens);
```

Une réponse est faite de **contenus** : du texte, ou des demandes d'outils (`FunctionCallContent`). **Aucune demande d'outil = mission terminée.**

### 3.4 Exécuter les outils demandés

```csharp
foreach (var call in calls)
    results.Add(new FunctionResultContent(call.CallId, await InvokeAsync(call, cancellationToken)));

history.Add(new ChatMessage(ChatRole.Tool, results));
```

- Le modèle peut demander **plusieurs outils d'un coup** : on les exécute tous.
- Chaque résultat porte le **même `CallId`** que sa demande. C'est grâce à lui que le modèle sait quel résultat répond à quelle question.
- Les résultats sont ajoutés avec le rôle **`Tool`**.

### 3.5 Les erreurs ne font pas planter la boucle

```csharp
if (!_toolsByName.TryGetValue(call.Name, out var tool))
    return $"Erreur : l'outil '{call.Name}' n'existe pas.";

try
{
    return await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    return $"Erreur de l'outil '{call.Name}' : {ex.Message}";
}
```

- **Outil inconnu** (le modèle invente `delete_everything`) : on le lui **dit**, au lieu de planter.
- **Outil en erreur** (accès refusé, fichier absent) : le message de l'erreur devient le résultat. Le modèle le lit et **change d'approche**, par exemple en essayant un autre fichier.
- **Sauf l'annulation** (`OperationCanceledException`) : si l'utilisateur appuie sur Ctrl+C, on ne doit surtout pas l'attraper. Elle doit arrêter toute la boucle.

### 3.6 La limite de tours

```csharp
for (var iteration = 1; iteration <= maxIterations; iteration++) { ... }
return new AgentRunResult(AgentRunStatus.MaxIterationsReached, null, maxIterations, history, totalTokens);
```

Sans limite, un modèle qui tourne en rond (demander le même fichier encore et encore) coûterait de l'argent **indéfiniment**. Au bout de 10 tours par défaut, on s'arrête avec le statut `MaxIterationsReached`.

## 4. Tester sans payer : le faux modèle

`ScriptedChatClient` est un **faux modèle**. On lui donne d'avance la liste des réponses qu'il doit faire. Il enregistre aussi tout ce qu'on lui envoie, pour qu'on puisse vérifier.

```csharp
var client = new ScriptedChatClient(
    ToolCall("list_files", new() { ["path"] = "." }, callId: "call_42"),   // 1ʳᵉ réponse : demande un outil
    Text("Il y a un fichier."));                                           // 2ᵉ réponse : conclut
```

Pourquoi ? Parce que le vrai modèle est **payant**, **lent**, et ne répond **jamais deux fois exactement pareil**. Avec un faux modèle, les tests sont gratuits, instantanés, et donnent toujours le même résultat.

C'est possible parce que la boucle ne connaît pas Claude : elle parle à une **interface**, `IChatClient`. Le vrai Claude et le faux modèle la respectent tous les deux.

## 5. Les 9 scénarios vérifiés

| Scénario | Ce qu'il garantit |
|---|---|
| Le modèle répond directement | Une seule itération, statut `Completed` |
| Le but et les outils sont envoyés | Le premier message est le but ; les outils `list_files`, `read_file` sont proposés |
| L'outil est exécuté et renvoyé | Le résultat porte le bon `CallId` et le rôle `Tool` |
| L'historique est gardé intact | La réponse du modèle est renvoyée telle quelle |
| Outil inconnu | Le modèle est prévenu, la boucle continue |
| Accès refusé | Le modèle reçoit « hors du workspace », la boucle continue |
| Limite de tours | Arrêt à 3 tours, statut `MaxIterationsReached` |
| Tokens | 30 + 12 = 42 |
| Annulation | Ctrl+C pendant un outil arrête tout |

## 6. Vérifie toi-même

```bash
dotnet run --project spikes/AgentLoop.Tests -- -class "AgentYattus.Spikes.Tests.AgentLoopTests"
```

Résultat attendu : `Total: 9, Failed: 0`. Avec les tests de la leçon 03 : 17 sur 17.

## 7. Quiz

1. Pourquoi renvoie-t-on tout l'historique au modèle à chaque tour ?
2. Comment la boucle sait-elle que la mission est terminée ?
3. À quoi sert le `CallId` ?
4. Pourquoi ne faut-il jamais attraper l'`OperationCanceledException` ?
5. Pourquoi une limite de tours ?

## 8. Pour aller plus loin

- Building effective agents (Anthropic) : <https://www.anthropic.com/engineering/building-effective-agents>
- Tool use, le protocole : <https://docs.claude.com/en/docs/agents-and-tools/tool-use/overview>

<details>
<summary>Réponses du quiz</summary>

1. Parce que le modèle n'a pas de mémoire entre deux appels : l'historique est sa seule mémoire.
2. Quand sa réponse ne contient aucune demande d'outil.
3. À relier chaque résultat à la demande d'outil à laquelle il répond.
4. Parce qu'elle signifie que l'utilisateur veut tout arrêter. L'attraper ferait continuer la boucle malgré lui.
5. Pour qu'un modèle qui tourne en rond ne coûte pas de l'argent indéfiniment.

</details>
