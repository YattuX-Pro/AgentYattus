# Leçon 74 — Importer la galerie au démarrage, sans rien figer dans le code

> **Commit** : `feat(team): import de la galerie au premier démarrage`
> **Phase** : le bureau des agents
> **Fichiers** : `Api/Modules/Team/PortraitSeed.cs`, `appsettings.json`, `deploy/portraits/`, `tests/…/TeamTests.cs`

## 1. Ce qu'on construit

Les 22 personnages générés pour la maquette (deux poses chacun) sont livrés dans `deploy/portraits/`, avec un `portraits.json` : prénoms suggérés, chef d'équipe, portrait de « Vous », équipe de départ. Au démarrage, **si la galerie est vide**, l'API importe ce dossier.

## 2. Des données de départ, pas du code

Les prénoms « Inès », « Malik » ou « Léon » n'apparaissent nulle part dans le C# : ils sont dans un fichier de données. Après l'import, tout se change dans l'admin. Le dossier ne sert qu'une fois.

```csharp
if (await db.Portraits.AnyAsync(cancellationToken))
    return; // une galerie qui contient déjà un portrait n'est jamais touchée
```

Rejouer l'import ne détruit donc jamais le travail d'un administrateur.

## 3. Un service hébergé, dans l'ordre

`PortraitSeeder` est un `IHostedService` ajouté **après** celui qui applique les migrations du module : les services hébergés démarrent dans l'ordre où on les ajoute, donc les tables existent quand l'import commence.

Le chemin est relatif au projet de l'API (`ContentRootPath`), pas au dossier d'où l'on lance la commande : `dotnet run` depuis la racine ou depuis `src/` donnent le même résultat.

## 4. Un dossier abîmé n'empêche pas de démarrer

Fichier manquant, JSON invalide, image qui n'a pas la bonne signature : l'import échoue, un avertissement part dans les journaux, et l'API démarre avec une galerie vide. Un administrateur peut toujours ajouter des portraits depuis l'admin. Les fichiers passent le même contrôle (signature, 2 Mo) qu'un envoi depuis l'admin.

## 5. Les tests partent d'une galerie vide

`ApiFactory` met `Team:SeedFolder` à vide : chaque test ajoute les portraits dont il a besoin, et ne dépend pas des 44 images du dépôt. Un test dédié vérifie l'import sur un petit dossier temporaire.

## 6. Quiz

1. Pourquoi l'import ne fait-il rien si la galerie contient déjà un portrait ?
2. Pourquoi l'ordre d'ajout des services hébergés compte-t-il ?

<details>
<summary>Réponses du quiz</summary>

1. Pour ne jamais écraser ce qu'un administrateur a fait dans l'admin : l'import ne sert qu'au premier démarrage.
2. Les services hébergés démarrent dans cet ordre : celui qui crée les tables doit passer avant celui qui les remplit.

</details>
