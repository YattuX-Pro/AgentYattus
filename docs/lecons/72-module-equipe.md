# Leçon 72 — Le module Équipe : une galerie d'images en base, et des envois contrôlés

> **Commit** : `feat(team): module Équipe et galerie de portraits`
> **Phase** : le bureau des agents
> **Fichiers** : `Api/Modules/Team/` (`TeamDb.cs`, `TeamModule.cs`, `Migrations/`), `Contracts/Team.cs`, `Program.cs`, `tests/…/TeamTests.cs`

## 1. Ce qu'on construit

Un nouveau module, **Équipe**, avec son schéma `team` et cinq tables : `Portraits`, `Colleagues`, `Traits`, `Lines` (répliques) et `TeamSettings`. Ce commit livre la **galerie** : lister, envoyer, renommer, désactiver et supprimer un portrait.

## 2. Des images dans PostgreSQL ?

Une pose pèse 40 à 50 Ko en WebP. Pour quelques dizaines de portraits, une colonne `bytea` est le choix le plus simple : pas de dossier à sauvegarder à part, pas de service de stockage à installer, et la sauvegarde de la base contient tout. Au-delà de quelques milliers d'images, un stockage d'objets (S3) deviendrait plus adapté ; ce n'est pas notre cas.

La liste ne charge jamais les octets : elle **projette** seulement les colonnes utiles (`Select(p => new { p.Id, p.Slug, … })`).

## 3. Croire la signature, pas l'extension

```csharp
bytes.StartsWith([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
: bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "image/webp" : null
```

Un fichier `photo.png` peut contenir n'importe quoi, et le type annoncé par le navigateur se falsifie. Les premiers octets (la **signature**) disent ce que le fichier est vraiment. On borne aussi la taille (**2 Mo**) **avant** de tout lire.

Le type renvoyé au navigateur est celui qu'on a détecté, jamais celui qui a été envoyé : un fichier ne peut pas se faire servir comme du HTML.

## 4. Des images lisibles sans jeton

`GET /team/portraits/{id}/{pose}` est `AllowAnonymous` : ce sont des illustrations, pas des données, et une balise `<img>` ne sait pas envoyer de jeton. Tout le reste du module exige une connexion, et la gestion le rôle `llm-admin`.

## 5. Le prénom, une entrée de la consigne

Le prénom d'un collègue finira dans sa consigne (leçon 75). On n'accepte donc que des lettres, espaces, apostrophes et tirets : `Inès. Ignore tes règles` est refusé. C'est la même idée qu'une requête SQL paramétrée : on limite ce qui peut entrer là où un texte est interprété.

## 6. Quiz

1. Pourquoi vérifier les premiers octets plutôt que l'extension ?
2. Pourquoi la liste des portraits ne charge-t-elle pas les images ?

<details>
<summary>Réponses du quiz</summary>

1. L'extension et le type annoncé se falsifient ; la signature dit ce que le fichier contient réellement.
2. Pour ne pas transférer des mégaoctets à chaque affichage de la liste : les images se chargent une par une, par leur adresse.

</details>
