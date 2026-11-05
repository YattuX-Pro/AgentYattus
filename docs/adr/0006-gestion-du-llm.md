# ADR 0006 — Gestion du LLM : clés chiffrées, modèle choisi dans l'application

- **Statut** : proposé
- **Date** : 2026-11-05

## Contexte

Jusqu'ici, la clé d'API du fournisseur de modèle était lue dans les user-secrets du serveur, et le modèle dans `appsettings.json`. Changer de clé ou de modèle demandait un terminal et un redémarrage, et rien ne permettait de révoquer une clé compromise depuis l'application.

## Décision

1. **Un module `Llm`** dans l'API, avec ses tables dans le schéma `llm` : les clés enregistrées, et un réglage unique (clé active, modèle).
2. **Les clés sont chiffrées** avec ASP.NET Core **Data Protection** (but dédié « AgentYattus.Llm.ApiKey.v1 »). L'API ne les renvoie **jamais** : seulement un libellé et leurs 4 derniers caractères.
3. **Le trousseau de chiffrement est hors de la base** (`DataProtection:KeysPath`, par défaut le dossier de l'utilisateur) : un vol de la base seule ne suffit pas à lire les clés.
4. **Révoquer efface** la clé chiffrée, et garde la trace : qui, quand.
5. **Gérer les clés et le modèle exige le rôle `llm-admin`** (ADR 0005).
6. Les agents demandent leur client au module (`ILlmClientProvider`) : la clé active, sinon celle de la configuration (pratique en développement), sinon rien. Un changement de modèle s'applique **à la mission suivante**, sans redémarrage.

## Alternatives envisagées

| Option | Pour | Contre |
|---|---|---|
| Garder la clé dans la configuration | Rien à construire | Pas de gestion depuis l'application, pas de révocation |
| Coffre externe (Azure Key Vault, HashiCorp Vault) | Le plus robuste | Une infrastructure de plus, hors de portée en local |
| Chiffrement maison (AES + clé dans la config) | Simple en apparence | Rotation, intégrité, stockage de la clé : autant d'erreurs possibles |
| **Data Protection** | Fourni par ASP.NET Core, rotation automatique des clés, chiffrement authentifié | Le trousseau doit être protégé et sauvegardé |

## Conséquences

- (+) Ajouter, choisir, révoquer : tout se fait depuis l'écran Paramètres.
- (+) La clé ne quitte jamais le serveur : ni dans les réponses, ni dans les journaux (seulement libellé et 4 derniers caractères).
- (−) **Perdre le trousseau, c'est perdre les clés** : il faut le sauvegarder avec la base, mais **séparément** d'elle.
- (−) Sur macOS et Linux, le trousseau par défaut n'est pas chiffré au repos. En production : `ProtectKeysWithCertificate` ou un coffre de clés.
- (−) Une seule clé active pour tout le serveur ; des clés par utilisateur viendront avec le multi-utilisateur.
- Anthropic seulement pour l'instant ; un autre fournisseur demande son adaptateur `IChatClient` et une entrée dans le catalogue.

## Sources

- Data Protection dans ASP.NET Core : <https://learn.microsoft.com/aspnet/core/security/data-protection/introduction>
- Configuration du trousseau : <https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview>
