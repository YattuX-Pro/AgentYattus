# Leçon 36 — Le mode Gratuit : une chaîne de modèles, et la bascule automatique

> **Commit** : `feat(llm): mode Gratuit, chaîne de modèles et bascule automatique`
> **Phase** : fournisseurs et modèles
> **Fichiers** : `Modules/Llm/` (`FallbackChatClient.cs`, `LlmModule.cs`, `LlmProviders.cs`, `models-snapshot.json`, migration), `Modules/Agents/AgentFrameworkRunner.cs`, `Contracts/Llm.cs`, `Client.Core/` (`SettingsViewModel.cs`, `ApiClient.cs`, `MissionViewModel.cs`), `Desktop/Views/SettingsView.axaml`, `scripts/update-models-snapshot.py`, tests (`FreeModeTests.cs`, `SettingsViewModelTests.cs`)

## 1. Ce qu'on construit

Un **mode Gratuit**, dans l'écran Modèles :

- une **chaîne** de modèles gratuits, que tu ordonnes (↑) ;
- quand le modèle en cours répond « quota épuisé », la mission **passe au suivant**, toute seule, et y reste ;
- **jamais vers un modèle payant** : la chaîne refuse tout modèle qui ne l'est pas ;
- le journal de la mission affiche chaque bascule : « Quota épuisé, bascule : nvidia/… ».

On ajoute aussi **NVIDIA** (16 modèles gratuits avec outils), et chaque fournisseur peut porter un **avertissement** affiché avant de s'y connecter.

> **Une image** : un voyageur avec plusieurs cartes de transport gratuites. Quand l'une est épuisée pour la journée, il sort la suivante, sans s'arrêter. Mais il n'a pas de carte bancaire dans la poche : il ne paiera jamais un billet par erreur.

## 2. D'où vient le « gratuit »

| Source | Combien | Ce qu'il faut savoir |
|---|---|---|
| OpenCode Zen | 10 modèles | Gratuits pour une durée limitée ; tes requêtes peuvent servir à améliorer ces modèles |
| OpenRouter (`:free`) | 12 modèles | Limités en débit ; leurs hébergeurs peuvent conserver tes requêtes |
| NVIDIA | 16 modèles | Accès d'essai, limité en débit |
| Ollama, LM Studio | ce que tu installes | Vraiment gratuit, et rien ne quitte ta machine |
| Gemini, Groq | offres à quota | Le catalogue les affiche à leur prix payant |

⚠️ **« Prix 0 » ne veut pas toujours dire gratuit.** Certains fournisseurs du catalogue affichent 0 parce que les modèles sont **inclus dans un abonnement payant** (les « token plans »). On ne les a pas branchés.

⚠️ **Ce que le mode Gratuit ne fait pas** : créer plusieurs comptes pour cumuler les quotas. C'est interdit par les conditions de tous les fournisseurs, et c'est le meilleur moyen de faire bannir sa clé. Ici, une connexion = un compte, le tien.

## 3. Le client qui bascule

```csharp
public sealed class FallbackChatClient(IReadOnlyList<LlmClient> chain) : IChatClient
{
    private int _current;

    public async Task<ChatResponse> GetResponseAsync(...)
    {
        while (true)
        {
            try
            {
                return await chain[_current].Client.GetResponseAsync(history, options, cancellationToken);
            }
            catch (Exception ex) when (IsExhausted(ex, cancellationToken) && Next())
            {
                // Le modèle suivant reprend la même conversation.
            }
        }
    }
```

C'est le motif **décorateur** : `FallbackChatClient` est lui-même un `IChatClient`, qui en enveloppe plusieurs. L'exécuteur des agents ne voit **aucune différence** : il parle à « un modèle », qui se trouve être une chaîne.

- `catch … when (…)` : un **filtre d'exception**. On n'attrape que les erreurs de quota, et seulement s'il reste un modèle. Sinon l'erreur remonte telle quelle.
- **On reste sur le modèle suivant** (`_current` ne revient pas en arrière) : inutile de réessayer à chaque tour un modèle dont le quota est épuisé.
- La **conversation entière** passe au modèle suivant : il reprend là où le précédent s'est arrêté, outils compris, parce que tous parlent le même protocole.

## 4. Qu'est-ce qu'un quota épuisé ?

```csharp
internal static bool IsExhausted(Exception ex, CancellationToken cancellationToken) => !cancellationToken.IsCancellationRequested && ex switch
{
    ClientResultException { Status: 0 or 429 or 402 or 503 } => true,
    TaskCanceledException => true,
    HttpRequestException { StatusCode: null or HttpStatusCode.TooManyRequests or ... } => true,
    _ => false,
};
```

| Code | Sens |
|---|---|
| 429 *Too Many Requests* | Quota ou débit dépassé : le cas principal |
| 402 *Payment Required* | Crédit gratuit épuisé |
| 503 *Service Unavailable* | Service saturé |
| 0, ou pas de code | Le serveur ne répond pas (Ollama éteint) |

Une erreur **400** (requête refusée) ne déclenche **pas** de bascule : ce n'est pas un problème de quota, et la cacher derrière un autre modèle masquerait un vrai bug. Un test le vérifie.

## 5. La garantie « jamais payant »

```csharp
if (error is null && !await IsFreeAsync(selection, db, catalog, cancellationToken))
    error = $"{selection.Model} n'est pas gratuit : la chaîne n'accepte que des modèles gratuits.";
```

La garantie est dans le **serveur**, pas dans l'écran. Un modèle est gratuit si le catalogue lui donne un prix nul, ou s'il tourne sur un serveur **local**. Une adresse « Compatible OpenAI » ne compte pas : on ne sait pas ce qu'elle facture.

Et l'ordre de préférence de la leçon 35 se complète : un modèle **choisi exprès pour un rôle** l'emporte sur la chaîne. Si tu mets le Rédacteur sur Claude Sonnet, c'est ton choix explicite ; tous les autres restent gratuits.

## 6. Une petite liste dans une colonne JSON

```csharp
public string FreeChain { get; set; } = "[]";

[NotMapped]
public List<LlmSelectionDto> Chain { get => ...Deserialize...; set => FreeChain = ...Serialize...; }
```

La chaîne compte dix modèles au plus, se lit et s'écrit d'un bloc, et n'est jamais interrogée par morceaux : une table de plus n'apporterait rien. La migration donne `[]` comme valeur par défaut aux lignes existantes ; sans cela, la lecture d'un texte vide aurait échoué.

## 7. L'écran

- Une carte **Mode gratuit** : un interrupteur, la chaîne (avec ↑ et Retirer), **＋ Modèle gratuit** (le sélecteur ne montre alors que les modèles gratuits) et **Tout ajouter** (les modèles locaux d'abord, puis les autres).
- Dans la fenêtre **Connecter**, l'avertissement du fournisseur s'affiche en doré avant la clé.

## 8. Les tests

| Test | Vérifie |
|---|---|
| `An_exhausted_model_hands_over_to_the_next_and_stays_there` | Bascule, et le modèle épuisé n'est pas réessayé |
| `Other_errors_and_an_exhausted_chain_are_not_hidden` | Une erreur 400 remonte ; une chaîne entièrement épuisée aussi |
| `A_switch_is_written_in_the_mission_journal` | De bout en bout : `bascule:nvidia/b` dans le journal |
| `The_chain_accepts_only_free_models` | Un modèle payant est refusé ; révoquer une connexion la retire de la chaîne |
| `Agents_use_the_chain_unless_they_have_their_own_model` | Chaîne pour l'Analyste, modèle propre pour le Rédacteur |
| `The_free_chain_only_offers_free_models_…` | Le sélecteur filtré, « Tout ajouter », l'interrupteur |

## 9. Vérifie toi-même

Connecte Ollama (gratuit, local) et OpenRouter. **Tout ajouter**, active le mode Gratuit, lance une mission. Coupe Ollama pendant qu'elle tourne : le journal affiche la bascule vers le modèle OpenRouter, et la mission continue.

## 10. Quiz

1. Pourquoi `FallbackChatClient` implémente-t-il lui-même `IChatClient` ?
2. Pourquoi ne pas basculer sur une erreur 400 ?
3. Où est appliquée la garantie « jamais payant », et pourquoi pas seulement dans l'écran ?
4. Pourquoi un modèle « Compatible OpenAI » ne peut-il pas entrer dans la chaîne ?
5. Pourquoi ne faut-il pas créer plusieurs comptes pour cumuler les quotas ?

## 11. Pour aller plus loin

- Les filtres d'exception (`when`) : <https://learn.microsoft.com/dotnet/csharp/language-reference/statements/exception-handling-statements#exception-filters>
- Le motif décorateur avec Microsoft.Extensions.AI (`DelegatingChatClient`) : <https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai>
- Les codes HTTP 429 et 402 : <https://developer.mozilla.org/fr/docs/Web/HTTP/Status/429>
- OpenRouter, modèles gratuits et limites : <https://openrouter.ai/docs/api-reference/limits>

<details>
<summary>Réponses du quiz</summary>

1. Pour que l'exécuteur des agents l'utilise comme n'importe quel modèle, sans rien savoir de la chaîne : c'est le motif décorateur.
2. Parce qu'une 400 n'est pas un quota : c'est une requête refusée. La cacher derrière un autre modèle masquerait un vrai problème.
3. Dans le serveur, qui refuse tout modèle non gratuit. L'écran peut être contourné ; l'API, non.
4. Parce qu'on ne sait pas ce que cette adresse facture : seuls le prix nul du catalogue et les serveurs locaux prouvent la gratuité.
5. C'est interdit par les conditions des fournisseurs, et cela fait bannir la clé.

</details>
