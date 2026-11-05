# Leçon 08 — Passer à Agent Framework

> **Commit** : `feat(spike): la même mission avec Agent Framework`
> **Phase** : 1, Agent Framework
> **Fichiers** : `spikes/AgentLoop/FrameworkAgent.cs`, `spikes/AgentLoop.Tests/FrameworkAgentTests.cs`, `spikes/AgentLoop/Program.cs`

## 1. Ce qu'on construit

Un agent qui explore un dossier et répond à une question, exactement comme le spike de la phase 0. La différence, c'est que **la boucle n'est plus écrite à la main** : on la confie à Microsoft Agent Framework.

> **Une image** : à la phase 0, on apprend à conduire en boîte manuelle. On sait ce que fait chaque pédale. À la phase 1, on passe en boîte automatique : on conduit plus sereinement, mais on sait ce que la boîte fait à notre place.

## 2. Rappel : c'est quoi, la boucle d'agent ?

Un modèle de langage (Claude, par exemple) ne sait **rien faire d'autre que produire du texte**. Il ne peut pas lire un fichier lui-même. Il peut seulement *demander* à lire un fichier. Voici le mécanisme :

```
 Toi :       « Que contient ce dossier ? »  + la liste des outils disponibles
 Modèle :    « J'aimerais appeler list_files(path: ".") »        ← une DEMANDE d'outil
 Ton code :  exécute list_files, obtient ["a.txt"]
 Ton code :  renvoie au modèle « résultat de list_files : ["a.txt"] »
 Modèle :    « Le dossier contient un fichier, a.txt. »           ← réponse finale, la boucle s'arrête
```

Ce va-et-vient s'appelle **la boucle d'agent**. Tant que le modèle demande des outils, on les exécute et on lui renvoie les résultats. Quand il répond par du texte, c'est fini.

**Vocabulaire**

| Mot | Sens |
|---|---|
| **Outil** (*tool*) | Une fonction de ton code que le modèle peut demander à appeler, comme `list_files` ou `read_file`. |
| **Demande d'outil** (`FunctionCallContent`) | Le message où le modèle dit « appelle tel outil avec tels arguments ». |
| **Résultat d'outil** (`FunctionResultContent`) | Ce que ton code renvoie au modèle après avoir exécuté l'outil. |
| **CallId** | L'identifiant qui relie un résultat à sa demande, par exemple `call_42`. |
| **Tour** (*itération*) | Un appel au modèle. |

## 3. Le problème

Écrire la boucle soi-même est formateur, mais dans un vrai produit il faut aussi gérer :

- les demandes d'outils multiples dans une même réponse ;
- un outil qui n'existe pas, ou qui plante ;
- une limite de tours, pour qu'un agent ne tourne pas indéfiniment et ne coûte pas une fortune ;
- l'historique de la conversation, la télémétrie et, plus tard, l'approbation humaine.

Réécrire tout ça pour chaque agent, c'est long et source de bugs.

## 4. Les options

| Option | Pour | Contre |
|---|---|---|
| Garder notre boucle maison | Contrôle total, aucune dépendance | Tout à maintenir nous-mêmes : erreurs, historique, approbations, télémétrie |
| **Microsoft Agent Framework** | Officiel chez Microsoft, construit sur `Microsoft.Extensions.AI` que l'on utilise déjà, fournit agents, outils, approbations et workflows | Une dépendance de plus, et des comportements à découvrir |
| Utiliser directement le SDK Anthropic | Accès à toutes les fonctions de Claude | On s'enferme chez un seul fournisseur de modèles |

## 5. Le choix

**Agent Framework**, version **1.26.0** (paquet `Microsoft.Agents.AI`). C'est la décision prise dès le cadrage du projet. Ce spike sert à **vérifier** qu'il se comporte comme notre boucle sur les mêmes scénarios, et à repérer les différences.

## 6. Le code expliqué

### 6.1 Créer l'agent — `FrameworkAgent.cs`

```csharp
var withTools = chatClient.AsBuilder()
    .UseFunctionInvocation(configure: invoker =>
    {
        invoker.MaximumIterationsPerRequest = maxIterations;
        invoker.IncludeDetailedErrors = true;
    })
    .Build();
```

- `chatClient` est la connexion au modèle (Claude, ou un faux modèle dans les tests).
- `AsBuilder()...Build()` empile des **couches** autour de cette connexion, comme des poupées russes.
- `UseFunctionInvocation` ajoute la couche qui **fait la boucle à notre place** : c'est le `FunctionInvokingChatClient`. Quand le modèle demande un outil, cette couche l'exécute et renvoie le résultat, toute seule.
- `MaximumIterationsPerRequest` : la limite de tours.
- `IncludeDetailedErrors = true` : quand un outil lève une erreur, le modèle reçoit son message, par exemple « Accès refusé : … hors du workspace ». Il peut alors s'adapter au lieu de deviner.
  ⚠️ C'est sans risque ici parce que nos messages d'erreur ont été écrits pour le modèle. Sur un outil dont les erreurs contiennent des secrets (une chaîne de connexion, par exemple), on ne l'activerait pas.

```csharp
return new ChatClientAgent(withTools, new ChatClientAgentOptions
{
    Name = "explorateur",
    ChatOptions = new ChatOptions { Tools = [.. tools] },
    UseProvidedChatClientAsIs = true,
});
```

- `ChatClientAgent` est l'**agent** : il a un nom, des outils et des options, et il garde l'historique.
- `UseProvidedChatClientAsIs = true` : par défaut, l'agent ajoute lui-même sa couche de boucle. Comme on a déjà mis la nôtre (pour régler la limite), on lui dit de ne rien ajouter. Sans ce réglage, il y aurait **deux boucles imbriquées**.

### 6.2 Lancer l'agent — `Program.cs`

```csharp
var response = await agent.RunAsync(args[1], cancellationToken: cancellation.Token);
Console.WriteLine(response.Text);
```

Un seul appel. `response.Text` contient la réponse finale, `response.Messages` toutes les étapes (demandes et résultats d'outils), et `response.Usage` les tokens consommés.

### 6.3 Les tests — `FrameworkAgentTests.cs`

On réutilise le **faux modèle** `ScriptedChatClient` : il renvoie des réponses écrites d'avance et enregistre tout ce qu'on lui envoie. Les tests sont donc **gratuits** (aucun appel à Claude), **rapides** et **déterministes** (même résultat à chaque fois).

Les 9 scénarios de la boucle maison, rejoués avec le framework :

| Scénario | Résultat |
|---|---|
| Le modèle répond directement | ✅ identique |
| Le but est envoyé en premier, avec les outils | ✅ identique |
| L'outil demandé est exécuté et son résultat renvoyé avec le bon CallId | ✅ identique |
| Les étapes sont rendues avec la réponse | ✅ identique |
| Un outil inconnu est signalé au modèle, sans plantage | ✅ identique |
| Un accès refusé (`../../etc/passwd`) est signalé au modèle | ✅ identique, grâce à `IncludeDetailedErrors` |
| Les tokens sont additionnés | ✅ identique |
| Une annulation (Ctrl+C) remonte | ✅ identique |
| **Limite de tours atteinte** | ⚠️ **différent**, voir ci-dessous |

### 6.4 La différence découverte

Avec notre boucle, quand la limite est atteinte, on s'arrête net et on renvoie le statut `MaxIterationsReached`.

Agent Framework fait autre chose : après le dernier tour autorisé, il **rappelle le modèle une fois de plus, sans lui proposer d'outils**. Le modèle ne peut alors plus rien demander : il est obligé de **conclure par du texte**. Avec une limite de 3, on observe donc **4 appels** au modèle.

```csharp
Assert.Equal(4, client.Requests.Count);
Assert.True(client.Options[3]?.Tools is null or { Count: 0 });   // 4ᵉ appel : aucun outil
Assert.Equal("Résumé de ce que j'ai vu.", response.Text);
```

**Conséquences pour Agent Yattus :**

- (+) L'utilisateur reçoit toujours une réponse, même partielle, au lieu d'un simple échec.
- (−) Il y a un appel de plus, donc un coût de plus.
- (−) La réponse ne dit pas qu'on a atteint la limite. Si on veut afficher « mission interrompue : limite atteinte », il faudra le détecter nous-mêmes (phase 2).

C'est exactement pour ça qu'on fait un spike : **découvrir ce comportement avant** de bâtir le vrai produit dessus.

## 7. Vérifie toi-même

Lancer les 9 tests du framework :

```bash
dotnet run --project spikes/AgentLoop.Tests -- -class "AgentYattus.Spikes.Tests.FrameworkAgentTests"
```

Résultat attendu : `Total: 9, Errors: 0, Failed: 0`.

Lancer une vraie mission sur Claude Haiku. C'est payant, quelques centimes, et il faut avoir enregistré ta clé avec `dotnet user-secrets` :

```bash
dotnet run --project spikes/AgentLoop -- spikes "Quels fichiers C# contient ce dossier ?" --framework
```

## 8. Quiz

1. Quelle couche exécute réellement les outils demandés par le modèle ?
2. Que se passerait-il si on oubliait `UseProvidedChatClientAsIs = true` ?
3. Avec `MaximumIterationsPerRequest = 5`, combien d'appels au modèle au maximum, et pourquoi ?
4. Pourquoi les tests utilisent-ils un faux modèle plutôt que Claude ?

## 9. Pour aller plus loin

- Agent Framework, vue d'ensemble : <https://learn.microsoft.com/agent-framework/overview/agent-framework-overview>
- Les outils dans Agent Framework : <https://learn.microsoft.com/agent-framework/agents/tools/>
- Microsoft.Extensions.AI (`IChatClient`) : <https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai>
- Le paquet utilisé : <https://www.nuget.org/packages/Microsoft.Agents.AI/1.26.0>

<details>
<summary>Réponses du quiz</summary>

1. Le `FunctionInvokingChatClient`, ajouté par `UseFunctionInvocation`.
2. L'agent ajouterait sa propre couche de boucle par-dessus la nôtre : deux couches empilées. Ce n'est pas ce qu'on veut, et le comportement devient plus difficile à prévoir (nous ne l'avons pas testé).
3. 6 appels : 5 tours avec outils, plus un dernier appel sans outils pour forcer une conclusion en texte.
4. Pour que les tests soient gratuits, rapides et donnent toujours le même résultat. Le vrai modèle peut répondre différemment à chaque fois.

</details>
