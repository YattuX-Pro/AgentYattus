# Leçon 16 — Un test qui protège l'architecture

> **Commit** : `test(architecture): frontières entre modules`
> **Phase** : 2, backend
> **Fichiers** : `tests/AgentYattus.Api.Tests/ArchitectureTests.cs`

## 1. Ce qu'on construit

Un test qui ne vérifie **aucune fonctionnalité**, mais une **règle d'organisation** du code : un module n'a le droit d'utiliser que ce que les autres modules **montrent**.

> **Une image** : dans un immeuble, chaque appartement a sa porte. On peut sonner chez le voisin (sa partie publique), pas passer par sa fenêtre pour se servir dans son frigo. Ce test, c'est le gardien qui fait la ronde.

## 2. Le problème

L'ADR 0003 dit : « un module n'utilise que les types publics d'un autre module ». Mais les trois modules vivent **dans le même projet**. Or en C#, `internal` veut dire « visible dans tout le projet ». Rien n'empêche donc le module Missions d'utiliser `AuditDb` directement, et d'écrire dans la table d'audit en contournant `AuditLog`.

Le compilateur ne voit pas le problème. Un relecteur fatigué non plus. **Un test, si.**

## 3. Les options

| Option | Pour | Contre |
|---|---|---|
| Un projet par module | Le compilateur fait respecter la règle | Beaucoup de projets pour trois petits modules |
| Une bibliothèque comme NetArchTest | Règles riches, lit aussi le corps des méthodes | Une dépendance de plus pour une seule règle |
| **Un petit test maison, par réflexion** | Quelques lignes, aucune dépendance | Ne voit pas tout (voir la limite plus bas) |

## 4. Le code expliqué

### 4.1 À quel module appartient un type ?

```csharp
private static string? ModuleOf(Type type) =>
    type.Namespace?.StartsWith("AgentYattus.Api.Modules.", StringComparison.Ordinal) == true
        ? type.Namespace["AgentYattus.Api.Modules.".Length..].Split('.')[0]
        : null;
```

On lit l'**espace de noms** : `AgentYattus.Api.Modules.Audit.AuditDb` appartient au module `Audit`.

### 4.2 Quels types un type utilise-t-il ?

```csharp
.. type.GetFields(all).Select(f => f.FieldType),
.. type.GetProperties(all).Select(p => p.PropertyType),
.. type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)),
.. type.GetConstructors(all).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)),
```

La **réflexion** permet à un programme d'examiner son propre code compilé : ses classes, champs, méthodes, paramètres… On liste tout ce qui apparaît dans les **signatures** d'un type.

`Flatten` ouvre aussi les types génériques : dans `IHubContext<MissionsHub>`, on regarde aussi `MissionsHub`.

### 4.3 La règle

```csharp
var violations =
    from type in moduleTypes
    from used in UsedTypes(type).Distinct()
    where ModuleOf(used) is { } other && other != ModuleOf(type) && !used.IsVisible
    select $"{type.FullName} -> {used.FullName}";

Assert.Empty(violations);
```

En français : **pour chaque type d'un module, s'il utilise un type d'un *autre* module qui n'est *pas public*, c'est une violation.** Le message d'échec nomme exactement le coupable, par exemple `MissionRunner -> AuditDb`.

## 5. Pourquoi ça marche avec l'injection de dépendances

```csharp
// Dépendances inter-modules en paramètres du constructeur : le test d'architecture les voit.
internal sealed class MissionRunner(MissionsDb db, AuditLog audit, IAgentRunner agent, ...)
```

Les dépendances d'une classe sont déclarées **dans son constructeur**. Le test les voit donc toutes.

## 6. La limite connue

```csharp
// ponytail: on lit les signatures (champs, propriétés, paramètres, bases), pas le corps des méthodes ;
// les dépendances injectées sont donc vues, un `new` dans une méthode non. Passer à NetArchTest si besoin.
```

Un `new AuditDb(...)` caché **à l'intérieur** d'une méthode échapperait au test. C'est acceptable tant que la règle d'équipe est « toute dépendance passe par le constructeur ». Si ça ne suffit plus, on passera à NetArchTest.

## 7. Vérifie toi-même

```bash
dotnet run --project tests/AgentYattus.Api.Tests -- -class "AgentYattus.Api.Tests.ArchitectureTests"
```

**Expérience** : ajoute temporairement `AuditDb auditDb` au constructeur de `MissionRunner`, relance le test, et lis le message d'échec. Puis annule ta modification.

## 8. Quiz

1. Pourquoi `internal` ne suffit-il pas à isoler les modules ?
2. Qu'est-ce que la réflexion ?
3. Quelle dépendance ce test ne peut-il pas voir ?
4. Pourquoi déclarer les dépendances dans le constructeur aide-t-il le test ?

## 9. Pour aller plus loin

- La réflexion en .NET : <https://learn.microsoft.com/dotnet/fundamentals/reflection/overview>
- NetArchTest : <https://github.com/BenMorris/NetArchTest>

<details>
<summary>Réponses du quiz</summary>

1. Parce que les trois modules sont dans le même projet, et `internal` rend un type visible dans tout le projet.
2. La capacité d'un programme à examiner son propre code compilé : types, champs, méthodes, paramètres.
3. Un objet créé avec `new` à l'intérieur d'une méthode, car le test ne lit que les signatures.
4. Parce que les paramètres du constructeur font partie de la signature, que le test inspecte.

</details>
