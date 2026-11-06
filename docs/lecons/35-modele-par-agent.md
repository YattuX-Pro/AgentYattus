# Leçon 35 — Un modèle par agent

> **Commit** : `feat(llm): un modèle par agent`
> **Phase** : fournisseurs et modèles
> **Fichiers** : `Modules/Llm/LlmModule.cs` (`ILlmAgentModels`, table `AgentModels`, migration), `Modules/Agents/` (`AgentsModule.cs`, `AgentFrameworkRunner.cs`), `Contracts/Agents.cs`, `Client.Core/` (`SettingsViewModel.cs`, `ApiClient.cs`, `MissionViewModel.cs`), `Desktop/Views/SettingsView.axaml`, tests

## 1. Ce qu'on construit

Dans l'écran **Modèles**, une section **Par agent** : l'Analyste et le Rédacteur peuvent chacun avoir **leur propre modèle**, ou suivre le modèle par défaut. Le journal d'une mission indique désormais quel modèle a travaillé : « Modèle : openrouter/google/gemma-4-31b-it:free ».

> **Une image** : dans une cuisine, on ne confie pas l'épluchage au chef étoilé. L'Analyste lit beaucoup de fichiers (beaucoup de tokens, peu de finesse) ; le Rédacteur écrit le rapport final (peu de tokens, beaucoup de finesse). Chacun son poste, chacun son coût.

## 2. Pourquoi c'est utile

- **Le coût** : l'Analyste consomme l'essentiel des tokens d'une revue. Lui donner un modèle gratuit ou peu cher, et garder un modèle fort pour le Rédacteur, divise la facture sans abîmer le rapport.
- **La confidentialité** : un Analyste sur Ollama lit ton code **sans qu'il quitte ta machine**.
- C'est ce que fait opencode, où chaque agent peut avoir son modèle.

## 3. Qui connaît quoi

Le module **Llm** connaît les modèles, mais pas les agents. Le module **Agents** connaît les rôles (« analyste », « redacteur »). C'est la frontière de l'ADR 0003 : Agents dépend de Llm, jamais l'inverse.

```csharp
public interface ILlmAgentModels
{
    Task<LlmSelectionDto?> GetAsync(string role, CancellationToken cancellationToken);
    Task<string?> SetAsync(string role, LlmSelectionDto? selection, string user, CancellationToken cancellationToken);
}
```

Le module Llm range un modèle **pour une clé de rôle**, sans savoir ce qu'elle veut dire. C'est le module Agents qui vérifie que le rôle existe :

```csharp
app.MapPut("/agents/{role}/model", async (...) =>
{
    if (AgentRoles.All.All(r => r.Key != role))
        return TypedResults.NotFound();
    ...
}).RequireAuthorization(Authentication.LlmAdminPolicy);
```

## 4. L'ordre de préférence

```csharp
if (await db.AgentModels.FindAsync([role], cancellationToken) is { } own
    && await db.Connections.FindAsync([own.ConnectionId], cancellationToken) is { RevokedAt: null } ownConnection)
    return (ownConnection, own.Model);
```

1. le modèle **propre au rôle**, si sa connexion n'est pas révoquée ;
2. sinon le **modèle par défaut** ;
3. sinon la clé Anthropic des user-secrets ;
4. sinon rien, et la mission échoue avec un message qui dit quoi faire.

Révoquer une connexion **efface** les choix des rôles qui l'utilisaient : ils reviennent au modèle par défaut, sans rester bloqués sur une connexion morte.

## 5. Chaque rôle demande son client

```csharp
var client = await llm.GetAsync(role.Key, cancellationToken) ?? throw new InvalidOperationException(...);
await audit.AppendAsync(missionId, $"modele:{client.Model}", cancellationToken);
```

Avant, l'exécuteur prenait un seul client au début de la mission. Maintenant, **chaque rôle** demande le sien au moment de travailler. Une revue peut donc faire lire l'Analyste sur Ollama, puis écrire le Rédacteur sur Claude Sonnet.

L'événement `modele:` entre dans le journal d'audit : on sait **après coup** quel modèle a produit quoi. C'est indispensable pour comparer la qualité, et ce sera la base du mode Gratuit (leçon 36).

## 6. Les tests

| Test | Vérifie |
|---|---|
| `Each_agent_can_have_its_own_model_and_falls_back_to_the_default` | Choix par rôle, rôle inconnu (404), modèle inconnu (400), retour au défaut quand la connexion est révoquée |
| `Only_llm_admins_can_choose_an_agent_model` | 403 sans le rôle `llm-admin` |
| `Each_agent_shows_its_own_model_and_can_get_a_new_one_or_go_back_to_the_default` | L'écran envoie `PUT /agents/{rôle}/model`, avec un modèle ou `null` |
| Tests de l'exécuteur et des approbations | Le journal contient `modele:` à chaque prise de main d'un rôle |

## 7. Vérifie toi-même

Connecte Ollama et Anthropic. Mets l'Analyste sur `qwen3:8b`, laisse le Rédacteur par défaut sur Claude. Lance une revue : le journal de la mission affiche les deux modèles, l'un après l'autre.

## 8. Quiz

1. Pourquoi donner un modèle moins cher à l'Analyste plutôt qu'au Rédacteur ?
2. Pourquoi le module Llm ne vérifie-t-il pas que le rôle existe ?
3. Que devient le choix d'un rôle si sa connexion est révoquée ?
4. À quoi sert l'événement `modele:` dans le journal ?

## 9. Pour aller plus loin

- opencode, modèles par agent : <https://opencode.ai/docs/agents/>
- La frontière entre modules : leçon 16 et `docs/adr/0003-architecture-backend.md`

<details>
<summary>Réponses du quiz</summary>

1. Parce que l'Analyste lit beaucoup (beaucoup de tokens) et que la finesse compte surtout dans le rapport final.
2. Parce que les rôles appartiennent au module Agents : Llm ne doit pas en dépendre (ADR 0003). Agents vérifie le rôle avant d'appeler Llm.
3. Il est effacé : le rôle revient au modèle par défaut.
4. À savoir après coup quel modèle a travaillé, pour comparer la qualité et le coût.

</details>
