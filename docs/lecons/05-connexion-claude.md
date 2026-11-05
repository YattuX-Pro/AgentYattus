# Leçon 05 — Brancher le vrai modèle : Claude Haiku

> **Commit** : `feat(spike): connexion à Claude Haiku`
> **Phase** : 0, socle et boucle d'agent
> **Fichiers** : `spikes/AgentLoop/Program.cs`, `spikes/AgentLoop/AgentLoop.csproj`

## 1. Ce qu'on construit

Un petit programme en ligne de commande qui lance **une vraie mission** : on lui donne un dossier et une question, et Claude Haiku y répond en utilisant nos outils.

```bash
dotnet run --project spikes/AgentLoop -- spikes "Quels fichiers C# contient ce dossier ?"
```

## 2. Le problème

Jusqu'ici, la boucle ne parlait qu'à un faux modèle. Pour parler au vrai, il faut :

1. une **connexion** au service d'Anthropic ;
2. une **clé d'API**, qui prouve qui paie ;
3. garder cette clé **secrète**, même dans un dépôt public.

## 3. La connexion

```csharp
using IChatClient chatClient = new AnthropicClient { ApiKey = apiKey }
    .AsIChatClient("claude-haiku-5-5", defaultMaxOutputTokens: 16_000);
```

- `AnthropicClient` vient du **SDK officiel d'Anthropic** pour C#.
- `.AsIChatClient(...)` le transforme en `IChatClient`, **la même interface** que notre faux modèle. La boucle n'a donc **pas changé d'une ligne** : on a seulement branché un autre modèle.
- `claude-haiku-5-5` : le modèle le moins cher de la famille, suffisant pour ce spike.
- `defaultMaxOutputTokens` : la longueur maximale d'une réponse.

> **Une image** : `IChatClient` est une prise électrique standard. Le faux modèle et Claude sont deux appareils différents avec la même fiche. On peut brancher l'un ou l'autre sans recâbler la maison.

## 4. Le secret : la clé d'API

### Ce qu'il ne faut jamais faire

- écrire la clé dans le code ;
- la mettre dans `appsettings.json` ;
- la coller dans un chat, un ticket ou un message.

Une clé publiée sur GitHub est trouvée par des robots **en quelques minutes**, puis utilisée à tes frais.

### Ce qu'on fait : les *user-secrets*

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<ta-clé>" --project spikes/AgentLoop
```

La clé est rangée **dans ton dossier utilisateur**, hors du dépôt. Le projet la retrouve grâce à son identifiant :

```xml
<UserSecretsId>agent-yattus-spike-agentloop</UserSecretsId>
```

```csharp
var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
var apiKey = configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException("Clé d'API absente. Lance : dotnet user-secrets set ...");
```

Si la clé manque, le programme s'arrête avec un message qui **explique comment la fournir**.

⚠️ Les user-secrets servent au **développement** seulement. Sur un serveur, on utilisera des variables d'environnement ou un coffre de secrets (voir la leçon 10).

## 5. Le reste du programme

- **Ctrl+C** annule proprement la mission, grâce au `CancellationToken` transmis jusqu'à la boucle.
- À la fin, le programme affiche chaque étape :

```
→ list_files(path: .)
← ["AgentLoop", "AgentLoop.Tests"]
→ read_file(path: AgentLoop/Program.cs)
← using AgentYattus.Spikes; …

Statut : Completed · 3 appel(s) au modèle · 4210 tokens
Le dossier contient deux projets…
```

(Exemple illustratif : les chiffres varient d'une exécution à l'autre.)

## 6. Une remarque technique

```csharp
// Pas de Temperature, TopP ni TopK dans ChatOptions : l'API Anthropic les refuse et l'adaptateur les ignore.
```

Avec d'autres fournisseurs, on règle souvent la « température » (le degré de hasard). Ici, on ne le fait pas : ces réglages sont refusés pour ce modèle.

## 7. Vérifie toi-même

1. Enregistre ta clé avec la commande `dotnet user-secrets` ci-dessus. Tape-la dans **ton** terminal, jamais dans un chat.
2. Lance une mission. Elle est payante, mais coûte quelques centimes :

```bash
dotnet run --project spikes/AgentLoop -- spikes "Quels fichiers C# contient ce dossier ?"
```

## 8. Quiz

1. Pourquoi la boucle n'a-t-elle pas changé quand on est passé du faux modèle à Claude ?
2. Où est rangée la clé d'API avec les user-secrets ?
3. Que faire si une clé a été publiée par erreur ?

## 9. Pour aller plus loin

- SDK C# d'Anthropic : <https://github.com/anthropics/anthropic-sdk-csharp>
- Secrets en développement : <https://learn.microsoft.com/aspnet/core/security/app-secrets>

<details>
<summary>Réponses du quiz</summary>

1. Parce que la boucle dépend de l'interface `IChatClient`, que le faux modèle et Claude implémentent tous les deux.
2. Dans le dossier de l'utilisateur, hors du dépôt Git.
3. La révoquer immédiatement dans la console d'Anthropic, puis en créer une nouvelle. Supprimer le commit ne suffit pas : la clé a pu être copiée entre-temps.

</details>
