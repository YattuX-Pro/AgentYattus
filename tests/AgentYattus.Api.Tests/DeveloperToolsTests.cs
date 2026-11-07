using AgentYattus.Api.Modules.Agents;

namespace AgentYattus.Api.Tests;

/// <summary>Le mode développeur : chercher dans le code (leçon 122), modifier un passage (leçon 123).</summary>
public sealed class DeveloperToolsTests
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-dev-").FullName;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_workspace, path))!);
        File.WriteAllText(Path.Combine(_workspace, path), content);
    }

    [Fact]
    public void Search_finds_lines_and_skips_dependencies_secrets_and_links()
    {
        Write("src/OrderService.cs", "class OrderService\n{\n    var total = order.Result;\n}");
        Write("node_modules/lib/index.js", "order.Result");
        Write(".env", "ORDER_RESULT=secret");
        var outside = Directory.CreateTempSubdirectory("dehors-").FullName;
        File.WriteAllText(Path.Combine(outside, "x.cs"), "order.Result");
        Directory.CreateSymbolicLink(Path.Combine(_workspace, "lien"), outside);

        var found = new WorkspaceTools(_workspace, excluded: [".env"]).Search("ORDER.RESULT", cancellationToken: Ct);

        Assert.Equal($"src{Path.DirectorySeparatorChar}OrderService.cs:3: var total = order.Result;", found);
        Assert.Equal("Aucun résultat.", new WorkspaceTools(_workspace).Search("introuvable", cancellationToken: Ct));
        Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceTools(_workspace).Search("x", "..", Ct));
    }

    [Fact]
    public async Task Edit_replaces_one_exact_passage_and_refuses_missing_or_ambiguous_ones()
    {
        Write("a.cs", "var x = task.Result;\nvar y = 2;\n");
        Write("b.cs", "f();\nf();\n");
        var tools = new WorkspaceTools(_workspace);

        await tools.EditFile("a.cs", "var x = task.Result;", "var x = await task;", Ct);

        Assert.Equal("var x = await task;\nvar y = 2;\n", await File.ReadAllTextAsync(Path.Combine(_workspace, "a.cs"), Ct));
        Assert.StartsWith("Passage introuvable", (await Assert.ThrowsAsync<InvalidOperationException>(() => tools.EditFile("a.cs", "absent", "x", Ct))).Message);
        Assert.StartsWith("Passage présent plusieurs fois", (await Assert.ThrowsAsync<InvalidOperationException>(() => tools.EditFile("b.cs", "f();", "g();", Ct))).Message);
        Assert.IsType<Microsoft.Extensions.AI.ApprovalRequiredAIFunction>(tools.AsAIFunctions().Single(f => f.Name == "edit_file"));
    }
}
