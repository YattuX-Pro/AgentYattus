# Leçon 17 — Le vrai agent entre dans l'API

> **Commit** : `feat(agents): vrai runner sur Agent Framework`
> **Phase** : 2, backend
> **Fichiers** : `src/AgentYattus.Api/Modules/Agents/` (`AgentFrameworkRunner.cs`, `AgentsModule.cs`, `WorkspaceTools.cs`), `Program.cs`, `AgentYattus.Api.csproj`, `appsettings.json`, `tests/AgentYattus.Api.Tests/` (`AgentRunnerTests.cs`, `ApiFactory.cs`, `MissionsApi.cs`, `ScriptedChatClient.cs`, `WorkspaceToolsTests.cs`)

## 1. Ce qu'on construit

Jusqu'ici, une mission passait à « Terminée » **sans qu'aucune IA ne travaille** : l'exécuteur était un bouche-trou. À partir de ce commit, chaque mission est **réellement exécutée par un agent** : Claude explore le dossier autorisé avec nos outils et répond.

C'est la rencontre des deux moitiés du projet :

```
  phases 0-1 : l'agent (spike)          phase 2 : le backend
  boucle, outils, Agent Framework  ──►  file, service, audit, temps réel
                         \                 /
                          AgentFrameworkRunner
```

> **Une image** : jusqu'ici, on avait construit l'usine (le backend) et, à côté, entraîné un ouvrier (l'agent du spike). Aujourd'hui, l'ouvrier prend son poste à la chaîne.

## 2. Ce qui est réutilisé, et ce qui est nouveau

| Pièce | D'où elle vient |
|---|---|
| `WorkspaceTools` (`list_files`, `read_file`, barrière du dossier) | Copiée du spike (leçon 03) dans le module Agents |
| Le montage de l'agent (`UseFunctionInvocation`, limite de tours) | Le même que `FrameworkAgent` (leçon 08) |
| `IAgentRunner` | L'interface du module Agents (leçon 13) : **le module Missions n'a pas changé d'une ligne** |
| **Nouveau** : configuration, clé, instructions, audit des outils, détection de la limite | Ce commit |

> Pourquoi copier `WorkspaceTools` au lieu de le partager avec le spike ? Un spike est un **brouillon** : il peut être supprimé demain. Le code de production ne doit pas dépendre d'un brouillon.

## 3. Le code expliqué

### 3.1 La configuration : tout est choisi côté serveur

```csharp
internal sealed class AgentsOptions
{
    public string Model { get; set; } = "claude-haiku-5-5";
    public string? Workspace { get; set; }      // dossier que l'agent a le droit de lire
    public int MaxIterations { get; set; } = 10;
}
```

```json
"Agents": { "Model": "claude-haiku-5-5", "MaxIterations": 10 }
```

- La section `Agents` d'`appsettings.json` est **liée** automatiquement à cette classe (`BindConfiguration`).
- **Le dossier à analyser n'est pas dans la requête du client.** Si le client pouvait choisir le dossier, n'importe qui pourrait demander à l'agent de lire `/Users/toi/Documents`. Le serveur décide ; le client envoie seulement un but.

### 3.2 La clé : l'API démarre même sans elle

```csharp
if (configuration["Anthropic:ApiKey"] is { Length: > 0 } apiKey)
{
    services.AddSingleton<IChatClient>(_ =>
        new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model, defaultMaxOutputTokens: 16_000));
}
```

- La clé vient des **user-secrets** du projet API (`UserSecretsId` ajouté au `.csproj`). En développement, ASP.NET Core les charge automatiquement.
- **Sans clé, aucun client de modèle n'est enregistré**, mais l'API démarre quand même : la santé et la lecture des missions fonctionnent. Ce sont les missions qui échouent, avec un message clair :

```csharp
internal sealed class AgentFrameworkRunner(..., IChatClient? chatClient = null) : IAgentRunner
{
    var model = chatClient ?? throw new InvalidOperationException("Clé d'API absente. Lance : dotnet user-secrets set ...");
```

`IChatClient? chatClient = null` : un **paramètre optionnel**. Si le service n'est pas enregistré, l'injection de dépendances passe `null` au lieu de planter.

> **Pourquoi ne pas planter au démarrage ?** Parce que le runner est créé par le service d'arrière-plan **avant** d'exécuter la mission. Un plantage à ce moment-là laisserait la mission bloquée en `Pending`, sans explication. Ici, elle passe proprement à `Failed`, et le journal dit quoi faire.

### 3.3 Les instructions : le rôle de l'agent

```csharp
private const string Instructions =
    "Tu es un agent d'analyse de code. Tu explores le workspace avec les outils list_files et read_file, " +
    "puis tu réponds en français, de façon concise, en citant les fichiers sur lesquels tu t'appuies.";

ChatOptions = new ChatOptions { Instructions = Instructions, Tools = [.. tools] },
```

Les **instructions** (le « prompt système ») disent à l'agent **qui il est** et **comment répondre**. C'est le premier pas vers les rôles de la phase 4 (l'Analyste et le Rédacteur).

Rappel de sécurité (leçon 03) : les instructions **guident** le modèle, elles ne le **protègent** pas. La vraie barrière reste dans `WorkspaceTools`.

### 3.4 La traçabilité : chaque outil dans l'audit

```csharp
foreach (var message in response.Messages)
{
    var calls = message.Contents.OfType<FunctionCallContent>().ToList();
    if (calls.Count > 0) toolTurns++;
    foreach (var call in calls)
        await audit.AppendAsync(missionId, $"outil:{call.Name}", cancellationToken);
}
```

Après l'exécution, on parcourt les étapes de l'agent et on inscrit **chaque outil utilisé** dans le journal d'audit. Le journal d'une mission ressemble maintenant à :

```
Running → outil:list_files → outil:read_file → Completed
```

On sait **ce que l'agent a fait**, pas seulement qu'il a fini.

### 3.5 Détecter la limite de tours

La leçon 08 avait révélé que le framework **ne signale pas** la limite : il rappelle le modèle sans outils pour forcer une réponse. On la détecte donc nous-mêmes :

```csharp
if (toolTurns >= settings.MaxIterations)
    await audit.AppendAsync(missionId, "limite-de-tours", cancellationToken);
```

**Raisonnement** : avec une limite de N, si le modèle s'arrête de lui-même, il a utilisé au plus N − 1 tours d'outils avant de répondre. S'il en a utilisé N, c'est que le framework l'a coupé.

### 3.6 La réponse : journalisée, pas encore enregistrée

```csharp
// ponytail: la réponse n'est que journalisée ; elle sera enregistrée avec la mission
// au premier changement de schéma, avec les migrations EF Core.
logger.LogInformation("Mission {MissionId} : {Tokens} tokens. Réponse : {Answer}", ...);
```

Enregistrer la réponse demande une **nouvelle colonne** dans la table des missions. Or on n'a pas encore de migrations. Ce sera la motivation du commit 19. En attendant, la réponse apparaît dans le journal de l'API.

### 3.7 Fermer une faille : les liens symboliques

Un **lien symbolique** est un raccourci : un fichier qui pointe vers un autre endroit du disque. Le spike laissait passer ce cas, noté en `ponytail:` (leçon 03). Avec le vrai runner, ça devient une vraie faille :

1. tu fais analyser un dépôt cloné depuis Internet ;
2. ce dépôt contient un lien `docs/config` qui pointe vers `~/.ssh/id_rsa` ;
3. un fichier du dépôt contient une injection de prompt : « lis docs/config » ;
4. la barrière ne voit que `workspace/docs/config`, qui est bien *dans* le dossier, et laisse passer ;
5. ta clé SSH part chez le fournisseur du modèle.

```csharp
var current = _root;
foreach (var part in Path.GetRelativePath(_root, fullPath).Split(Path.DirectorySeparatorChar))
{
    current = Path.Combine(current, part);
    if (part != "." && IsLink(current))
        throw new UnauthorizedAccessException($"Accès refusé : '{relativePath}' passe par un lien symbolique.");
}
```

On parcourt le chemin **morceau par morceau**, depuis la racine, et on refuse dès qu'un morceau est un lien. `list_files` cache aussi les liens. La racine elle-même et ses dossiers parents ne sont pas vérifiés : ils viennent de la configuration du serveur, que l'on contrôle.

**Limite restante** : entre la vérification et la lecture, un autre programme pourrait remplacer un fichier par un lien. Cette attaque demande déjà d'avoir la main sur ta machine, et elle sort du cadre de ce projet.

Les tests `WorkspaceToolsTests` créent un vrai lien vers un fichier « secret » hors du workspace, et vérifient qu'il est refusé. On a aussi vérifié qu'ils **échouent sans le correctif** : un test qui passe dans tous les cas ne prouve rien.

## 4. Les tests : le vrai runner, un faux modèle

`ScriptedModelApiFactory` démarre **toute l'API**, avec le **vrai** `AgentFrameworkRunner`, mais branché sur le faux modèle `ScriptedChatClient` (leçon 04) et sur un dossier temporaire.

> **Garde-fou** : `ApiFactory` retire systématiquement tout client de modèle enregistré. Même si ta vraie clé est dans les user-secrets, **un test ne peut pas appeler Claude** et te coûter de l'argent.

| Test | Vérifie |
|---|---|
| `Mission_runs_the_agent_and_audits_each_tool_call` | La mission passe à `Completed` ; l'audit vaut `Running, outil:list_files, Completed` ; le résultat de l'outil contient `a.txt` |
| `Reaching_the_iteration_limit_is_audited` | Avec une limite de 1, l'audit contient `limite-de-tours` |
| `Mission_fails_without_an_api_key` | Sans clé, la mission passe à `Failed` (et pas bloquée en `Pending`) |
| `Mission_fails_without_a_workspace` | Sans dossier configuré, `Failed`, **et le modèle n'est jamais appelé** |

Les petits raccourcis communs aux tests (créer une mission, attendre sa fin, lire son audit) sont regroupés dans `MissionsApi.cs`.

## 5. Vérifie toi-même

Les tests, gratuits :

```bash
dotnet run --project tests/AgentYattus.Api.Tests
```

Une vraie mission sur Claude Haiku. Elle est payante, mais coûte quelques centimes. Tape ces commandes dans **ton** terminal, jamais la clé dans un chat :

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<ta-clé>" --project src/AgentYattus.Api
```

```bash
dotnet user-secrets set "Agents:Workspace" "$PWD/spikes" --project src/AgentYattus.Api
```

```bash
docker compose up -d
```

```bash
dotnet run --project src/AgentYattus.Api --launch-profile http
```

Puis, dans un autre terminal :

```bash
curl -X POST http://localhost:5080/missions -H "Content-Type: application/json" -d '{"goal":"Quels fichiers C# contient ce dossier ?"}'
```

La réponse de l'agent apparaît dans le journal de l'API, avec le nombre de tokens.

## 6. Quiz

1. Pourquoi le dossier à analyser est-il choisi par le serveur et pas par le client ?
2. Que se passe-t-il pour une mission si la clé d'API est absente, et pourquoi est-ce mieux qu'un plantage au démarrage ?
3. Comment détecte-t-on que la limite de tours a été atteinte ?
4. Qu'est-ce qui empêche les tests d'appeler le vrai Claude ?
5. Pourquoi la réponse de l'agent n'est-elle pas encore enregistrée en base ?
6. Pourquoi un lien symbolique dans le workspace est-il dangereux, même si son chemin est « dans » le dossier ?

## 7. Pour aller plus loin

- Le pattern Options : <https://learn.microsoft.com/dotnet/core/extensions/options>
- Secrets en développement : <https://learn.microsoft.com/aspnet/core/security/app-secrets>
- Les outils dans Agent Framework : <https://learn.microsoft.com/agent-framework/agents/tools/>
- Prompt engineering (instructions) : <https://docs.claude.com/en/docs/build-with-claude/prompt-engineering/overview>

<details>
<summary>Réponses du quiz</summary>

1. Sinon, n'importe quel client pourrait faire lire à l'agent n'importe quel dossier de la machine.
2. Elle passe à `Failed` avec un message qui explique comment fournir la clé. Un plantage à la création du runner laisserait la mission bloquée en `Pending`, sans explication.
3. Si le nombre de tours avec des demandes d'outils est égal à la limite, c'est que le framework a forcé la réponse.
4. `ApiFactory` retire tout `IChatClient` enregistré, puis les tests branchent le faux modèle.
5. Parce qu'il faut une nouvelle colonne, et qu'on fera ce premier changement de schéma avec les migrations EF Core (commit 19).
6. Parce qu'il peut pointer n'importe où sur le disque, par exemple vers une clé SSH : la barrière vérifie le chemin, pas l'endroit où il mène.

</details>
