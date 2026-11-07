# Leçon 92 — La revue de sécurité de la phase

> **Commit** : `fix(security): journal, envoi d'images et requêtes incomplètes`
> **Phase** : le bureau des agents
> **Fichiers** : `Agents/AgentFrameworkRunner.cs`, `Audit/AuditModule.cs`, `Team/` (`TeamModule.cs`, `Colleagues.cs`, `TeamContent.cs`, `PortraitSeed.cs`), tests

## 1. Ce que la revue a trouvé

Un agent indépendant a relu tout le code de la phase. Rien de grave : **un point moyen**, quatre faibles. Tous sont corrigés ici.

## 2. Le point moyen : une ligne de journal qui bloque une mission

Le chemin d'un appel d'outil vient du **modèle**, donc potentiellement d'un fichier piégé du workspace. Deux textes pouvaient faire échouer l'écriture dans PostgreSQL :

- une **moitié de paire** UTF-16 isolée (`\uD800`), que PostgreSQL refuse ;
- un émoji **coupé en deux** par la limite de 120 caractères.

Pire : l'événement refusé restait suivi par EF Core, et **chaque** ajout suivant le renvoyait et échouait, jusqu'au statut final de la mission, qui restait « en cours ».

Corrections :

```csharp
var runes = target.EnumerateRunes()  // une moitié isolée devient U+FFFD
    .Where(r => Rune.GetUnicodeCategory(r) is not (Control or Format or LineSeparator or ParagraphSeparator));
```

On coupe par caractère complet (`Rune`), jamais au milieu d'une paire. Le **nom d'outil**, que le modèle peut inventer, est limité à des minuscules et des « _ » (sinon « ? »). Et `AuditLog` détache un événement refusé : un échec ne contamine plus les suivants.

## 3. Les points faibles

| Problème | Correction |
|---|---|
| La taille d'un envoi n'était vérifiée qu'après lecture complète du formulaire | `RequestSizeLimit` et `MultipartBodyLengthLimit` à 5 Mo sur la route : refus avant lecture |
| Une image de 2 Mo peut annoncer 30 000 × 30 000 px (des gigaoctets une fois décodée) | La taille est lue dans l'**en-tête** (PNG : IHDR ; WebP : VP8, VP8L, VP8X), sans décoder ; 2 048 px au plus |
| Images publiques sans `X-Content-Type-Options` | `nosniff` : le navigateur ne devine pas un autre type |
| Un champ JSON absent provoquait une erreur 500 | `?? ""` et `?? []` : une erreur de validation claire à la place |

## 4. Ce qui a été vérifié et tient

Pas d'IDOR sur les collègues (le couple projet/collègue est vérifié), historique filtré par les droits, gestion réservée aux administrateurs, type d'image venu de la signature (aucun SVG ni HTML possible), prénom sans retour à la ligne avant d'entrer dans la consigne, répliques qui ne vont jamais au modèle, aucun `innerHTML` dans l'admin.

## 5. Quiz

1. Pourquoi un événement refusé bloquait-il toute la suite de la mission ?
2. Pourquoi lire la taille d'une image dans son en-tête plutôt que la décoder ?

<details>
<summary>Réponses du quiz</summary>

1. EF Core le gardait comme « à insérer » : chaque SaveChanges suivant essayait de nouveau, échouait, et l'erreur remontait.
2. Décoder une image géante pour la mesurer, c'est justement subir l'attaque ; l'en-tête donne la taille en quelques octets.

</details>
