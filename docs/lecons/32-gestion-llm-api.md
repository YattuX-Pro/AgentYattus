# Leçon 32 — Gérer le LLM : clés chiffrées, modèle, révocation

> **Commit** : `feat(llm): clés d'API chiffrées, choix du modèle et révocation`
> **Phase** : authentification et gestion du LLM
> **Fichiers** : `src/AgentYattus.Api/Modules/Llm/` (`LlmModule.cs`, migration), `Contracts/Llm.cs`, `Modules/Agents/` (`AgentFrameworkRunner.cs`, `AgentsModule.cs`), `Program.cs`, `docs/adr/0006-gestion-du-llm.md`, tests (`LlmTests.cs`, `ApiFactory.cs`), `evals/…/ModelEval.cs`

## 1. Ce qu'on construit

L'API sait maintenant **gérer les clés d'API et le modèle** utilisés par les agents :

| Route | Effet |
|---|---|
| `GET /llm` | Fournisseurs et modèles (avec leurs prix), clés enregistrées, clé active, modèle |
| `POST /llm/credentials` | Ajouter une clé |
| `DELETE /llm/credentials/{id}` | **Révoquer** une clé |
| `PUT /llm/settings` | Choisir la clé active et le modèle |

Toutes exigent le rôle **`llm-admin`**. L'écran viendra au commit suivant.

## 2. La règle d'or d'un secret : le serveur le garde, personne ne le relit

Une fois ajoutée, une clé ne **ressort plus jamais** de l'API, ni dans les réponses, ni dans les journaux. On l'affiche par un **libellé** et ses **4 derniers caractères** (« Clé perso …WXYZ ») : de quoi la reconnaître, rien de quoi s'en servir. C'est le principe qu'appliquent GitHub, Stripe ou la console Anthropic.

## 3. Chiffrer avec Data Protection

```csharp
public static IDataProtector Protector(IDataProtectionProvider protection) =>
    protection.CreateProtector("AgentYattus.Llm.ApiKey.v1");
...
ProtectedKey = LlmCredential.Protector(protection).Protect(apiKey),
Last4 = apiKey[^4..],
```

**ASP.NET Core Data Protection** chiffre et authentifie des données avec des clés qu'il gère lui-même (génération, rotation tous les 90 jours). On n'écrit pas de cryptographie : on l'utilise.

- Le **but** (« purpose ») isole les usages : une donnée protégée pour un autre but ne se déchiffre pas ici.
- En base, la colonne contient seulement un texte chiffré, qui commence par `CfDJ8…`.

### Le trousseau, hors de la base

```csharp
if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
```

Le **trousseau** (les clés de chiffrement) est rangé **ailleurs** que la base. Celui qui vole la base seule n'a que du texte chiffré. Par défaut, il est dans le dossier de l'utilisateur.

⚠️ Deux conséquences importantes, notées dans l'ADR 0006 :

- **Perdre le trousseau, c'est perdre les clés** : il faut le sauvegarder, séparément de la base.
- Sur macOS et Linux, le trousseau par défaut n'est pas lui-même chiffré : en production, on le protège par un certificat ou un coffre de clés.

## 4. Révoquer, vraiment

```csharp
// Révoquer, c'est effacer la clé chiffrée : même avec le trousseau, plus rien à déchiffrer.
credential.ProtectedKey = null;
credential.RevokedAt = DateTimeOffset.UtcNow;
credential.RevokedBy = http.User.Identity?.Name ?? "?";
```

Révoquer ne se contente pas de « désactiver » : la clé chiffrée est **effacée**. La ligne reste, pour la trace : qui l'a ajoutée, qui l'a révoquée, quand.

Révoquer deux fois ne fait pas d'erreur (204) : on dit que l'opération est **idempotente**. Révoquer la clé active la désactive : les missions suivantes échoueront avec un message clair, plutôt que d'utiliser une clé révoquée.

⚠️ Révoquer **dans Agent Yattus** empêche Agent Yattus de s'en servir. Si la clé a fuité, il faut **aussi** la révoquer dans la console Anthropic.

## 5. Valider ce qui entre

```csharp
if (apiKey.Length is < 16 or > 512 || apiKey.Any(char.IsWhiteSpace))
    errors["apiKey"] = ["La clé doit faire entre 16 et 512 caractères, sans espace."];
```

Le fournisseur doit exister, le libellé faire 60 caractères au plus, et le modèle choisi appartenir au catalogue du fournisseur de la clé. Une clé révoquée ne peut pas redevenir active.

## 6. Les agents demandent leur modèle au module

```csharp
public interface ILlmClientProvider
{
    Task<LlmClient?> GetAsync(CancellationToken cancellationToken);
    Task<string> GetModelAsync(CancellationToken cancellationToken);
}
```

L'exécuteur ne reçoit plus un client figé au démarrage : il **demande** le client à chaque mission. Un changement de modèle ou de clé s'applique donc **à la mission suivante**, sans redémarrer.

L'ordre de préférence : la clé active choisie dans l'application, **sinon** celle des user-secrets (pratique en développement), **sinon** aucune, et la mission échoue avec un message qui dit quoi faire.

C'est encore la frontière entre modules de l'ADR 0003 : le module Agents ne connaît que l'interface publique ; les tables et le chiffrement restent privés au module Llm.

## 7. Les tests

Garde-fou renforcé : `ApiFactory` remplace le fournisseur de modèle par un **faux**, qui ne renvoie aucun client. Même avec une vraie clé enregistrée, aucun test ne peut appeler Anthropic.

| Test | Vérifie |
|---|---|
| `An_added_key_is_stored_encrypted_and_never_sent_back` | La clé n'apparaît dans aucune réponse ; en base, elle est chiffrée, et se déchiffre avec le bon but ; la première clé devient active |
| `Revoking_erases_the_encrypted_key_and_deactivates_it` | Effacée, désactivée, 204 deux fois, 404 si inconnue |
| `The_model_must_exist_and_the_key_must_not_be_revoked` | Modèle inconnu, clé révoquée : 400 |
| `Invalid_keys_are_refused` | Trop courte, avec des espaces : 400 |
| `Only_llm_admins_can_manage_keys` | Sans le rôle : 403, mais les missions restent accessibles |
| `The_agents_use_the_active_key_then_the_configuration_then_nothing` | L'ordre de préférence du fournisseur |

**Sur la vraie pile** (Keycloak, PostgreSQL, API) : `observateur` reçoit 403 ; `dev` ajoute une fausse clé, la réponse ne la contient pas, `GET /llm` montre « …ABCD » ; en base, la colonne ne contient que le texte chiffré `CfDJ8…`, et le trousseau est bien écrit dans le dossier configuré.

## 8. Quiz

1. Pourquoi l'API ne renvoie-t-elle jamais une clé enregistrée ?
2. Pourquoi ranger le trousseau hors de la base ?
3. Que se passe-t-il si on perd le trousseau ?
4. Pourquoi révoquer efface-t-il la clé chiffrée, au lieu de la marquer « inactive » ?
5. Pourquoi un changement de modèle s'applique-t-il sans redémarrer ?

## 9. Pour aller plus loin

- L'ADR : `docs/adr/0006-gestion-du-llm.md`
- Data Protection : <https://learn.microsoft.com/aspnet/core/security/data-protection/introduction>
- Gestion du trousseau : <https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview>

<details>
<summary>Réponses du quiz</summary>

1. Parce qu'un secret qui ressort peut fuiter (écran, journaux, navigateur) ; le libellé et 4 caractères suffisent à le reconnaître.
2. Pour qu'un vol de la base seule ne donne que du texte chiffré, inutilisable sans le trousseau.
3. Les clés enregistrées ne peuvent plus être déchiffrées : il faut les ressaisir.
4. Pour qu'il ne reste plus rien à déchiffrer, même pour quelqu'un qui aurait la base et le trousseau.
5. Parce que l'exécuteur demande le client au module Llm à chaque mission, au lieu de le recevoir une fois au démarrage.

</details>
