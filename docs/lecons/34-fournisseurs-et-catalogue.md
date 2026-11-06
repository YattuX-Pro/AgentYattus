# Leçon 34 — Tous les fournisseurs, un seul sélecteur : comme opencode

> **Commit** : `feat(llm): plusieurs fournisseurs et le catalogue models.dev`
> **Phase** : fournisseurs et modèles
> **Fichiers** : `src/AgentYattus.Api/Modules/Llm/` (`LlmProviders.cs`, `ModelCatalog.cs`, `LlmModule.cs`, `models-snapshot.json`, migration), `Contracts/Llm.cs`, `scripts/update-models-snapshot.py`, `Client.Core/` (`SettingsViewModel.cs`, `ApiClient.cs`), `Desktop/Views/SettingsView.axaml`, `docs/adr/0007-fournisseurs-et-catalogue.md`, tests (`LlmTests.cs`, `SettingsViewModelTests.cs`)

## 1. Ce qu'on construit

L'écran **Modèles** fonctionne maintenant comme [opencode](https://opencode.ai) :

1. **Connecter un fournisseur** : on le cherche dans une liste (Anthropic, OpenAI, Google Gemini, Mistral, Groq, DeepSeek, xAI, OpenRouter, OpenCode Zen, Ollama, LM Studio, ou « Compatible OpenAI »), puis on donne sa clé ou son adresse.
2. **Choisir un modèle** dans **un seul sélecteur** : tous les modèles de tous les fournisseurs connectés, avec une recherche, le contexte (« 200k »), le prix, et des pastilles « raisonne » et « gratuit ».

> **Une image** : avant, la voiture ne roulait qu'avec une marque d'essence. Maintenant, elle a un adaptateur universel, et un tableau de bord qui affiche toutes les pompes de la région avec leur prix.

## 2. Le problème

- Chaque fournisseur a son API. Écrire un adaptateur par fournisseur, c'est dix paquets à suivre.
- Les modèles et leurs prix changent chaque mois. Un catalogue écrit à la main est vite faux.
- Un serveur local (Ollama) n'a pas de catalogue : chacun y installe les modèles qu'il veut.

## 3. Deux protocoles suffisent

```csharp
public static IChatClient CreateClient(LlmProvider provider, string? apiKey, string baseUrl, string model) => provider.Wire switch
{
    LlmWire.Anthropic => new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model, defaultMaxOutputTokens: 16_000),
    _ => new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "local" : apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
        .GetChatClient(model).AsIChatClient(),
};
```

L'API d'OpenAI est devenue un **standard de fait** : Groq, Mistral, DeepSeek, Gemini, OpenRouter, Ollama… l'imitent. Il suffit de changer l'**adresse** (`Endpoint`). Avec l'adaptateur d'Anthropic, deux branches couvrent tous les fournisseurs.

Les deux donnent un `IChatClient` (leçon 05) : l'exécuteur des agents ne voit **aucune différence**. C'est tout l'intérêt d'une abstraction.

## 4. Le catalogue : models.dev, comme opencode

[models.dev](https://models.dev) est un catalogue ouvert de plus de 200 fournisseurs. opencode s'en sert ; nous aussi.

```csharp
// Un agent sans outils ne peut rien faire : on ne propose que les modèles qui savent en appeler.
if (!IsValidId(m.Name) || !Bool(m.Value, "tool_call") || Text(m.Value, "status") == "deprecated")
    continue;
```

Deux sources, un seul format :

- **Une copie embarquée** dans l'API (`models-snapshot.json`, 489 modèles), produite par `scripts/update-models-snapshot.py`. Elle sert hors ligne, et dans les tests.
- **La version en ligne**, chargée par le service `CatalogRefresher` au démarrage, puis chaque jour. Si models.dev ne répond pas, la copie reste.

⚠️ **Une donnée qui vient d'Internet n'est pas fiable.** On n'accepte que des identifiants faits de caractères sûrs (expression régulière), on retire les caractères de contrôle des noms, on borne les prix. Un catalogue piégé ne peut ni casser l'écran, ni glisser un faux identifiant.

On vérifie aussi la **forme** de chaque niveau du JSON (un objet là où on attend un objet). La revue de sécurité automatique l'a signalé (deux fois, et elle avait raison) : un catalogue mal formé levait une exception qui **arrêtait toute l'API**, car une exception non attrapée dans un `BackgroundService` arrête l'hôte par défaut. Désormais, toute erreur de mise à jour est journalisée, et l'ancien catalogue reste.

## 5. Les serveurs locaux annoncent leurs modèles

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/models");
```

Ollama et LM Studio répondent à `GET /v1/models` (le standard OpenAI) avec la liste des modèles installés. On l'affiche telle quelle, marquée **gratuit**. Si le serveur est éteint, l'écran le signale, et le reste s'affiche quand même.

## 6. Où la clé a le droit d'aller

La clé est envoyée à l'adresse de la connexion : cette adresse est donc **sensible**.

```csharp
if (uri.Scheme == "http" && !uri.IsLoopback)
    return "HTTP n'est accepté que sur cette machine (localhost) : utilise https://.";
```

- **HTTPS obligatoire**, sauf sur cette machine : en HTTP, la clé circulerait en clair sur le réseau.
- **Pas d'identifiants ni de paramètres** dans l'adresse (`https://user:pass@…`).
- **L'adresse d'un fournisseur à clé est imposée** : même si on demande `https://attacker.example.com`, une clé Anthropic ne part que chez Anthropic. Un test le vérifie.
- **Pas d'adresse link-local** (`169.254.169.254`, `fe80::`) : c'est là que les clouds servent les **métadonnées** d'une machine, parfois avec des identifiants. Faire appeler une adresse interne par le serveur s'appelle une **SSRF** (*Server-Side Request Forgery*).
- **Aucune redirection suivie** : sinon un serveur vérifié pourrait renvoyer la requête, et la clé, vers une adresse qui ne l'a pas été.
- **L'adresse est revérifiée à la connexion**, après la résolution DNS :

```csharp
ConnectCallback = async (context, cancellationToken) =>
{
    var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
    if (addresses.Length == 0 || addresses.Any(IsForbidden))
        throw new HttpRequestException($"Adresse non autorisée : {context.DnsEndPoint.Host}.");
    ...
    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
```

  Vérifier le texte de l'adresse ne suffit pas : `llm.exemple.com` peut pointer vers une adresse publique au moment de la vérification, puis vers `169.254.169.254` au moment de l'appel (*DNS rebinding*). On résout donc le nom soi-même, on vérifie **chaque** adresse obtenue, et on se connecte **à ces adresses-là**. `::ffff:169.254.169.254` (une adresse IPv4 déguisée en IPv6) est ramenée à sa forme IPv4 avant la vérification. Les appels au modèle passent par la même garde.
- **Réponse bornée à 1 Mo** : on lit au plus 1 Mo, même si le serveur n'annonce pas de taille. Un serveur qui répond sans fin ne peut pas remplir la mémoire de l'API.

## 7. Un choix = une connexion + un modèle

```csharp
public sealed record LlmSelectionDto(Guid ConnectionId, string Model);
```

opencode écrit `fournisseur/modèle`. Nous gardons l'identifiant de la **connexion**, parce qu'on peut avoir deux clés chez le même fournisseur (« Clé perso », « Clé équipe »).

## 8. Ne pas renommer ce qui marche

Le code parle maintenant de **connexions**, mais les tables gardent leurs noms de la leçon 32 :

```csharp
modelBuilder.Entity<LlmConnection>().ToTable("Credentials");
modelBuilder.Entity<LlmSettings>().Property(s => s.DefaultConnectionId).HasColumnName("ActiveCredentialId");
```

Résultat : la migration `AddConnectionBaseUrl` n'ajoute qu'**une colonne**. Renommer des tables pour le plaisir, c'est risquer des données pour rien.

## 9. L'écran

- **Le modèle par défaut**, en grand, avec un bouton **Changer**.
- **Les fournisseurs connectés**, en cartes : libellé, fournisseur, `…` et les 4 derniers caractères de la clé, ou l'adresse locale. **Révoquer** demande toujours une confirmation.
- **Le sélecteur** et la fenêtre **Connecter** sont des panneaux flottants, au-dessus de l'écran assombri. Après une connexion, le sélecteur s'ouvre tout seul, filtré sur ce fournisseur : on choisit tout de suite son modèle, comme dans opencode.

## 10. Les tests

| Test | Vérifie |
|---|---|
| `A_connected_key_is_stored_encrypted_and_never_sent_back` | La clé est chiffrée, jamais renvoyée |
| `Invalid_connections_are_refused` (11 cas) | Clé absente ou invalide, HTTP hors de la machine, adresse manquante, `ftp://`, identifiants dans l'adresse, adresses link-local |
| `Local_and_custom_servers_keep_their_address_but_online_providers_cannot_be_redirected` | Une clé Anthropic ne peut pas être envoyée ailleurs |
| `The_models_come_from_the_catalog_or_from_the_local_server` | Catalogue pour Anthropic, découverte pour Ollama (un faux serveur), erreur signalée pour LM Studio éteint |
| `The_agents_use_the_default_model_then_the_configuration_then_nothing` | Groq branché par le protocole OpenAI, à sa propre adresse |
| `The_catalog_keeps_tool_calling_models_and_refreshes_from_models_dev` | Les modèles sans outils, obsolètes ou mal nommés sont écartés |
| `A_malformed_catalog_never_breaks_the_current_one` (4 cas) | Un JSON de la mauvaise forme ne casse rien |
| `A_local_server_that_answers_too_much_is_ignored`, `A_huge_answer_without_announced_size_is_refused_after_the_limit` | Une réponse de plus d'1 Mo est refusée, annoncée ou non |
| `Link_local_addresses_are_refused_even_when_disguised` (6 cas) | Link-local, IPv4 déguisée en IPv6, `0.0.0.0` refusés ; réseau privé permis |
| `The_guarded_handler_reaches_a_real_local_server` | La garde laisse passer un vrai serveur local |
| `SettingsViewModelTests` (6) | Recherche, choix, connexion, clé oubliée après l'envoi, confirmation de révocation |

Aucun test n'appelle Internet : un faux serveur HTTP (`FakeHttp`) répond à la place.

## 11. Vérifie toi-même

```bash
ollama serve
```

```bash
ollama pull qwen3:8b
```

Dans l'application : **Connecter un fournisseur › Ollama › Connecter**. Le sélecteur s'ouvre et montre `qwen3:8b`, marqué gratuit. Choisis-le, lance une mission : l'agent tourne sur ta machine, sans clé et sans frais.

## 12. Quiz

1. Pourquoi deux adaptateurs suffisent-ils pour une dizaine de fournisseurs ?
2. Pourquoi ne garde-t-on que les modèles capables d'appeler des outils ?
3. À quoi sert la copie embarquée du catalogue, si on peut le charger en ligne ?
4. Pourquoi refuser `http://192.168.1.20:11434/v1` mais accepter `http://localhost:11434/v1` ?
5. Pourquoi l'adresse d'Anthropic est-elle imposée, même si on en envoie une autre ?

## 13. Pour aller plus loin

- L'ADR : `docs/adr/0007-fournisseurs-et-catalogue.md`
- opencode, fournisseurs et modèles : <https://opencode.ai/docs/providers/>
- models.dev : <https://models.dev>
- L'API compatible OpenAI d'Ollama : <https://github.com/ollama/ollama/blob/main/docs/openai.md>

<details>
<summary>Réponses du quiz</summary>

1. Parce que presque tous les fournisseurs imitent l'API d'OpenAI : il suffit de changer l'adresse. Anthropic a son propre adaptateur.
2. Parce qu'un agent travaille avec des outils (lire, lister, écrire) : un modèle qui ne sait pas les appeler ne peut rien faire.
3. À fonctionner hors ligne, et à faire tourner les tests sans Internet.
4. En HTTP, la clé circule en clair. Sur cette machine, rien ne passe par le réseau ; sur le réseau local, quelqu'un pourrait l'intercepter.
5. Pour qu'une clé Anthropic ne soit jamais envoyée à un autre serveur, même par erreur ou par une requête piégée.

</details>
