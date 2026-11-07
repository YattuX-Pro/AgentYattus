# Leçon 111 — Connecter un modèle sans espace admin

> **Commit** : `feat(desktop): « Modèles et clés » dans le logiciel en mode local`
> **Phase** : A
> **Fichiers** : `Client.Core/{SettingsViewModel,ApiClient,ShellViewModel}.cs`, `Desktop/Views/SettingsView.axaml`, `MainWindow.axaml`, `tests/.../SettingsViewModelTests.cs`

## 1. Le problème

En mode local, l'espace admin web ne tourne pas : il dépend de Keycloak (ADR 0014). Or c'est là qu'on connecte un fournisseur (une clé Anthropic, OpenRouter, Ollama…). Sans lui, une personne seule ne peut lancer aucune mission.

## 2. La décision

On **reprend l'écran « Modèles »** retiré à la leçon 68. Son code et ses tests sont dans l'historique git (`git show bd59f68^:…`). Il n'apparaît **qu'en mode local**, dans le menu de ton visage : « Modèles et clés ». En mode serveur, la gestion reste dans l'espace admin, réservé au propriétaire de l'instance.

Récupérer du code supprimé plutôt que le réécrire : c'est l'intérêt d'un historique propre, un commit par idée.

## 3. Ce que l'écran permet

- Connecter un fournisseur (clé d'API, serveur local, adresse compatible OpenAI) et révoquer une connexion.
- Choisir le modèle par défaut, puis un modèle par agent.
- Le **mode Gratuit** : une chaîne de modèles gratuits, avec bascule automatique quand le quota est atteint.

La clé part vers l'API locale, qui la chiffre (Data Protection, trousseau dans le dossier de données). Le logiciel ne la garde pas.

## 4. Vérifie toi-même

- `dotnet test --filter SettingsViewModelTests`.
- Dans le logiciel en mode local : ton visage › « Modèles et clés ».

## 5. Les limites

- L'écran n'a pas d'artboard dans la maquette « Bureau des agents ». Il garde ses composants d'origine, aux couleurs et polices du thème.

## 6. Quiz

1. Pourquoi ne pas montrer cet écran en mode serveur ?

<details>
<summary>Réponse</summary>

1. En mode serveur, les clés de l'instance sont l'affaire de son propriétaire (espace admin). Un utilisateur ne doit pas pouvoir les remplacer ni les révoquer.

</details>
