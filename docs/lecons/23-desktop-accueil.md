# Leçon 23 — L'application desktop : la coquille et l'écran Accueil

> **Commit** : `feat(desktop): coquille, jetons de design et écran Accueil`
> **Phase** : 3, client desktop
> **Fichiers** : `src/AgentYattus.Desktop/` (`Program.cs`, `App.axaml(.cs)`, `MainWindow.axaml`, `Theme/Tokens.axaml`, `Views/HomeView.axaml`)

## 1. Ce qu'on construit

La fenêtre d'Agent Yattus avec son premier écran, l'**Accueil** :

- un en-tête flottant (logo, navigation en pilule) ;
- les orbes des agents ;
- la question « Quelle mission aujourd'hui ? » et le champ de saisie ;
- des suggestions ;
- les cartes des missions récentes, qui changent de couleur **en direct** ;
- une barre d'état (backend connecté ou non).

Le tout fidèle à la maquette : thème sombre « observatoire », grille de points, pas de barre latérale, très peu de texte.

## 2. Avalonia en bref

**Avalonia** dessine l'interface **lui-même** (avec Skia), au lieu d'utiliser les contrôles du système. Le rendu est donc **identique** sur Windows, macOS et Linux (ADR 0001).

L'interface s'écrit en **XAML** (fichiers `.axaml`), un langage de balises :

```xml
<TextBlock Text="Quelle mission aujourd’hui ?" FontSize="40" FontWeight="Medium" HorizontalAlignment="Center" />
```

## 3. Les pièces

### 3.1 Le démarrage — `Program.cs` et `App.axaml.cs`

```csharp
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
```

`App.axaml.cs` **assemble** l'application : il crée le `HomeViewModel` (leçon 22) avec son client API, branche le hub, et le donne à la fenêtre comme `DataContext`.

```csharp
hub.MissionEventReceived += e => Dispatcher.UIThread.Post(() => vm.Apply(e));
```

Les événements du hub arrivent sur un **thread réseau**. Or seul le **thread de l'interface** (*UI thread*) a le droit de modifier l'écran. `Dispatcher.UIThread.Post` y renvoie le travail.

```csharp
await hub.StartAsync();
vm.IsBackendConnected = true;
await vm.LoadAsync();
```

**Nouveau dans ce commit** : la liste des missions est chargée **après** la connexion au hub. Dans l'autre ordre, un changement de statut survenu entre la lecture de la liste et la connexion serait perdu.

### 3.2 Les jetons de design — `Theme/Tokens.axaml`

```xml
<SolidColorBrush x:Key="Obsidienne" Color="#0E0F11" />
<SolidColorBrush x:Key="VertDeGris" Color="#5E9E92" />
<SolidColorBrush x:Key="Laiton" Color="#D8B86E" />
<SolidColorBrush x:Key="Cinabre" Color="#D9654B" />
```

Les couleurs de la maquette, **nommées une seule fois**. Les vues écrivent `{StaticResource Laiton}`, jamais `#D8B86E`. Changer une couleur, c'est changer une ligne. Chaque couleur a un **sens** :

| Couleur | Sens |
|---|---|
| Vert-de-gris | exécuté par le backend, actif |
| **Laiton** | **une décision humaine** |
| Cinabre | refusé, échec |

### 3.3 L'écran — `Views/HomeView.axaml`

**Les liaisons (*bindings*)** relient la vue au ViewModel :

```xml
<TextBox Text="{Binding Goal}" ... />
<Button Command="{Binding LaunchMissionCommand}" HotKey="Ctrl+Enter" ... />
```

- Taper dans le champ met à jour `Goal`.
- Le bouton exécute la commande, et se grise tout seul quand la commande est désactivée.
- `x:DataType="core:HomeViewModel"` active les **liaisons compilées** : une faute de frappe dans un nom de propriété est une **erreur de compilation**, pas un écran vide.

**Les styles par statut** :

```xml
<Border Classes="mission" Classes.completed="{Binding IsCompleted}" Classes.failed="{Binding IsFailed}"
        Classes.awaiting="{Binding IsAwaitingApproval}" ...>
```

```xml
<Style Selector="Border.mission.awaiting">
  <Setter Property="Background" Value="#0FD8B86E" />
  <Setter Property="BorderBrush" Value="#73D8B86E" />
</Style>
```

Une **classe** CSS-like s'ajoute ou se retire selon une propriété du ViewModel. Le style de la carte suit : vert qui pulse si active, laiton si elle attend ta décision (**nouveau**), cinabre si en échec.

**L'animation du halo** : un style `Ellipse.pulse` fait grossir et s'effacer un cercle en boucle, autour des missions actives.

### 3.4 Un vrai bug trouvé au rendu : les accents coupés

En vérifiant le rendu, les majuscules accentuées perdaient leur accent : « Ecrire », « A valider », « Echec ». Les minuscules (« sécurité ») s'affichaient bien.

**Le diagnostic, étape par étape** :

1. Le même texte avec une hauteur de ligne plus grande → l'accent revient. C'est donc un **découpage**, pas une police sans le caractère.
2. Avec `ClipToBounds = false` → l'accent revient aussi. Le bloc de texte **coupait** ce qui dépasse de ses bords.
3. L'accent d'une majuscule monte **au-dessus** de la hauteur prévue par la police, celui d'une minuscule non.

**La correction**, un style global dans `App.axaml` :

```xml
<Style Selector="TextBlock">
  <Setter Property="ClipToBounds" Value="False" />
</Style>
```

C'est la bonne démarche face à un bug d'affichage : **isoler** la cause par de petites expériences avant de corriger, plutôt que de deviner.

## 4. Comment vérifier un écran sans écran

La capture d'écran ne fonctionne pas dans l'environnement de travail utilisé ici. On utilise donc le **rendu headless** d'Avalonia (`Avalonia.Headless`) : un petit programme crée la vue avec un **faux réseau** qui renvoie des missions d'exemple, la dessine en mémoire et l'enregistre en PNG. On compare ensuite l'image à la maquette.

## 5. Vérifie toi-même

```bash
docker compose up -d
```

```bash
dotnet run --project src/AgentYattus.Api --launch-profile http
```

```bash
dotnet run --project src/AgentYattus.Desktop
```

Lance une mission : la carte apparaît, pulse, puis passe à « Terminée » ou « Échec » en direct.

## 6. Quiz

1. Pourquoi faut-il `Dispatcher.UIThread.Post` pour appliquer un événement du hub ?
2. À quoi servent les jetons de design ?
3. Qu'apportent les liaisons compilées (`x:DataType`) ?
4. Que signifie une carte couleur laiton ?
5. Comment a-t-on prouvé que les accents étaient coupés, et non absents de la police ?

## 7. Pour aller plus loin

- Documentation Avalonia : <https://docs.avaloniaui.net>
- Liaisons compilées : <https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings>
- Styles et sélecteurs : <https://docs.avaloniaui.net/docs/basics/user-interface/styling/>

<details>
<summary>Réponses du quiz</summary>

1. Parce que les événements arrivent sur un thread réseau, et que seul le thread de l'interface peut modifier l'écran.
2. À nommer chaque couleur une seule fois, avec son sens ; les vues y font référence, et un changement se fait en une ligne.
3. Une faute dans un nom de propriété devient une erreur de compilation au lieu d'un écran silencieusement vide.
4. Que la mission attend une décision humaine.
5. En augmentant la hauteur de ligne, puis en désactivant le découpage aux bords : dans les deux cas, l'accent réapparaît.

</details>
