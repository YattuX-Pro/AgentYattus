using System.Reflection;
using AgentYattus.Api.Modules.Missions;

namespace AgentYattus.Api.Tests;

public sealed class ArchitectureTests
{
    private const string ModulesNamespace = "AgentYattus.Api.Modules.";

    // Tous les modules sont dans le même assembly : `internal` ne les isole pas entre eux.
    // Règle : un module n'utilise que les types publics d'un autre module.
    // ponytail: on lit les signatures (champs, propriétés, paramètres, bases), pas le corps des méthodes ;
    // les dépendances injectées sont donc vues, un `new` dans une méthode non. Passer à NetArchTest si besoin.
    [Fact]
    public void Modules_only_use_public_types_of_other_modules()
    {
        var moduleTypes = typeof(MissionsModule).Assembly.GetTypes().Where(t => ModuleOf(t) is not null).ToList();

        var violations =
            from type in moduleTypes
            from used in UsedTypes(type).Distinct()
            where ModuleOf(used) is { } other && other != ModuleOf(type) && !used.IsVisible
            select $"{type.FullName} -> {used.FullName}";

        Assert.NotEmpty(moduleTypes);
        Assert.Empty(violations);
    }

    private static string? ModuleOf(Type type) =>
        type.Namespace?.StartsWith(ModulesNamespace, StringComparison.Ordinal) == true
            ? type.Namespace[ModulesNamespace.Length..].Split('.')[0]
            : null;

    private static IEnumerable<Type> UsedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        IEnumerable<Type> direct =
        [
            .. type.BaseType is { } baseType ? [baseType] : Array.Empty<Type>(),
            .. type.GetInterfaces(),
            .. type.GetFields(all).Select(f => f.FieldType),
            .. type.GetProperties(all).Select(p => p.PropertyType),
            .. type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)),
            .. type.GetConstructors(all).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)),
        ];
        return direct.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.IsGenericParameter)
            yield break;
        if (type.HasElementType)
        {
            foreach (var t in Flatten(type.GetElementType()!)) yield return t;
            yield break;
        }
        yield return type;
        foreach (var arg in type.GetGenericArguments())
            foreach (var t in Flatten(arg)) yield return t;
    }
}
