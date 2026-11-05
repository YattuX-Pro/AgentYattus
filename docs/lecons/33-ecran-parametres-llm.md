# Leçon 33 — L'écran Paramètres : choisir son modèle, gérer ses clés

> **Commit** : `feat(desktop): écran Paramètres, modèle et clés d'API`
> **Phase** : authentification et gestion du LLM
> **Fichiers** : `src/AgentYattus.Client.Core/` (`SettingsViewModel.cs`, `ApiClient.cs`, `ShellViewModel.cs`), `src/AgentYattus.Desktop/` (`Views/SettingsView.axaml`, `MainWindow.axaml`), `tests/AgentYattus.Client.Core.Tests/SettingsViewModelTests.cs`

## 1. Ce qu'on construit

Une icône de réglages en haut à droite, **visible seulement si tu as le rôle `llm-admin`**, ouvre l'écran **Modèle et clés d'API** :

- **à gauche**, les modèles, avec leur prix. Un clic enregistre le choix, qui servira à la mission suivante ;
- **à droite**, les clés, chacune avec son libellé, `…` suivi de ses 4 derniers caractères, son auteur, et son état (active, utilisable, révoquée) ;
- les actions **Utiliser** et **Révoquer**, cette dernière avec **confirmation** ;
- un formulaire **Ajouter une clé**, avec une saisie masquée.

## 2. Montrer ce qu'on a le droit de faire

```xml
<Button ... Command="{Binding ShowSettingsCommand}" IsVisible="{Binding IsLlmAdmin}">
```

`IsLlmAdmin` vient des rôles lus dans le jeton (leçon 31). Cacher le bouton améliore l'expérience : on ne propose pas une action qui serait refusée.

⚠️ Mais **cacher n'est pas protéger**. La vraie barrière est côté API, qui répond 403 sans le rôle (leçon 32). Un utilisateur qui fabriquerait la requête à la main serait refusé quand même.

## 3. Le secret ne fait que passer

```csharp
[RelayCommand(CanExecute = nameof(CanAdd))]
private async Task AddAsync(CancellationToken cancellationToken)
{
    var request = new AddLlmCredentialRequest("anthropic", NewLabel.Trim(), NewKey.Trim());
    NewKey = "";
    ...
```

- La clé est saisie dans un champ **masqué** (`PasswordChar="•"`).
- Elle est **effacée de l'écran dès l'envoi**, que l'ajout réussisse ou non. Elle ne reste pas affichée, ni en mémoire dans le ViewModel, plus longtemps que nécessaire.
- Elle n'est jamais relue : l'écran n'en connaît que les 4 derniers caractères.

## 4. Les actions irréversibles demandent une confirmation

```csharp
[RelayCommand] private void AskRevoke(LlmCredentialDto credential) => PendingRevoke = credential;
[RelayCommand] private void CancelRevoke() => PendingRevoke = null;
[RelayCommand] private Task ConfirmRevokeAsync() => ...
```

« Révoquer » n'agit pas tout de suite : il affiche un bandeau **« Révoquer « Clé équipe » ? La clé chiffrée est effacée du serveur. C'est définitif. »**, avec Annuler et Révoquer.

Après une révocation, l'écran rappelle de **révoquer aussi la clé chez le fournisseur** si elle a fuité : Agent Yattus ne peut effacer que sa propre copie.

## 5. Les erreurs de validation, en clair

```csharp
// Une erreur de validation (400) devient un message lisible ; les autres erreurs restent des exceptions.
private static async Task<string?> ErrorOrNullAsync(HttpResponseMessage response, CancellationToken cancellationToken)
```

Quand l'API refuse (clé trop courte, modèle inconnu), elle renvoie un **ProblemDetails** (RFC 9457) avec la raison. Le client en extrait le message, et l'affiche tel quel : « La clé doit faire entre 16 et 512 caractères, sans espace. »

## 6. Charger ne doit pas enregistrer

```csharp
partial void OnSelectedModelChanged(LlmModelDto? value)
{
    if (!_loading && value is not null)
        _ = SaveAsync(ActiveCredentialId, value.Id, $"Modèle : {value.Name}.");
}
```

Choisir un modèle l'enregistre aussitôt. Mais au **chargement** de l'écran, on positionne aussi `SelectedModel`, ce qui déclencherait un enregistrement inutile. Le drapeau `_loading` l'évite, et un test le vérifie.

## 7. Les tests

| Test | Vérifie |
|---|---|
| `Load_shows_models_keys_and_the_current_model_without_saving` | Modèles, clés, modèle courant ; **aucun** enregistrement au chargement |
| `Choosing_a_model_saves_it_with_the_active_key` | `PUT /llm/settings` avec le modèle et la clé active |
| `Adding_a_key_sends_it_once_then_forgets_it` | La clé part dans le `POST`, puis le champ est vidé |
| `A_rejected_key_shows_the_reason_and_is_still_forgotten` | Le message de l'API s'affiche ; la clé est quand même effacée |
| `Revoking_asks_for_confirmation_first` | Rien n'est envoyé avant la confirmation ; puis `DELETE`, et la clé apparaît révoquée |

L'écran a été rendu et vérifié : modèle choisi entouré de vert, clé active, clé révoquée en cinabre, bandeau de confirmation, champ masqué.

## 8. Vérifie toi-même

Connecte-toi avec `dev`, ouvre les réglages, ajoute une clé, choisis Sonnet, lance une mission : le journal de l'API indique le modèle utilisé. Puis connecte-toi avec `observateur` : l'icône de réglages n'apparaît pas.

## 9. Quiz

1. Pourquoi cacher l'icône de réglages ne suffit-il pas à protéger la gestion des clés ?
2. Pourquoi effacer le champ de la clé même quand l'ajout échoue ?
3. Pourquoi demander une confirmation avant de révoquer ?
4. Pourquoi faut-il aussi révoquer la clé chez le fournisseur si elle a fuité ?
5. À quoi sert le drapeau `_loading` ?

## 10. Pour aller plus loin

- ProblemDetails (RFC 9457) : <https://www.rfc-editor.org/rfc/rfc9457>
- Saisie de mot de passe dans Avalonia (`PasswordChar`) : <https://docs.avaloniaui.net/docs/reference/controls/textbox>

<details>
<summary>Réponses du quiz</summary>

1. Parce que n'importe qui peut fabriquer la requête à la main ; seule l'API, qui exige le rôle, protège réellement.
2. Pour qu'un secret ne reste pas affiché, ni en mémoire, plus longtemps que nécessaire.
3. Parce que l'action est irréversible : la clé chiffrée est effacée.
4. Parce qu'Agent Yattus n'efface que sa propre copie ; la clé reste valable chez le fournisseur tant qu'elle n'y est pas révoquée.
5. À éviter qu'un enregistrement parte quand l'écran positionne le modèle au chargement.

</details>
