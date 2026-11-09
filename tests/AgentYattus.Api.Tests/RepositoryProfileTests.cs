using AgentYattus.Api.Modules.Agents;

namespace AgentYattus.Api.Tests;

/// <summary>Le profil du dépôt (leçon 130) : ce qu'on en sait sans rien exécuter, avec les règles d'accès de search.</summary>
public sealed class RepositoryProfileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("profil-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void A_dotnet_repository_gets_its_languages_instructions_and_checks_without_secrets_or_dependencies()
    {
        Write("Orders.slnx", "<Solution />");
        Write("src/Orders/Orders.csproj", "<Project />");
        Write("src/Orders/OrderService.cs", "class OrderService {}");
        Write("tests/Orders.Tests/OrderServiceTests.cs", "class T {}");
        Write("AGENTS.md", "Les tests utilisent xUnit. Un fichier par classe.");
        Write("README.md", "# Orders");
        Write(".env", "SECRET=1");
        Write("node_modules/lib/index.js", "x");

        var profile = new WorkspaceTools(_root, excluded: [".env"]).Profile(TestContext.Current.CancellationToken);

        Assert.Equal(("C#", 2), profile.Languages[0]);
        Assert.Contains("Orders.slnx", profile.Projects);
        Assert.Equal(["dotnet build Orders.slnx", "dotnet test Orders.slnx"], profile.Checks.Select(c => c.Command));
        Assert.Equal(["AGENTS.md", "README.md"], profile.Instructions.Select(i => i.Path));
        var text = profile.Describe();
        Assert.Contains("Un fichier par classe.", text);
        // Les consignes du dépôt sont présentées comme des données, et ni le secret ni les dépendances n'apparaissent.
        Assert.Contains("données du dépôt", text);
        Assert.DoesNotContain(".env", text);
        Assert.Equal(6, profile.FileCount);
    }

    [Fact]
    public void Package_scripts_give_the_checks_with_the_right_runner_and_the_placeholder_test_is_skipped()
    {
        Write("package.json", """{ "scripts": { "build": "ng build", "test": "echo \"Error: no test specified\" && exit 1", "lint": "eslint ." } }""");
        Write("pnpm-lock.yaml", "");

        var checks = new WorkspaceTools(_root).Profile(TestContext.Current.CancellationToken).Checks;

        Assert.Equal(["pnpm run build", "pnpm run lint"], checks.Select(c => c.Command));
    }

    [Fact]
    public void A_folder_without_a_known_project_has_no_check_and_names_are_quoted_for_the_shell()
    {
        Assert.Empty(new WorkspaceTools(_root).Profile(TestContext.Current.CancellationToken).Checks);

        Write("Mon App.sln", "");
        Assert.Equal("dotnet test 'Mon App.sln'", new WorkspaceTools(_root).Profile(TestContext.Current.CancellationToken).Checks[1].Command);
    }

    [Fact]
    public async Task The_tool_reads_the_repository_again_each_time()
    {
        var tools = new WorkspaceTools(_root);
        Assert.Contains("aucun détecté", tools.ExploreRepository(TestContext.Current.CancellationToken));

        Write("go.mod", "module x");
        await Task.Yield();

        Assert.Contains("go test ./...", tools.ExploreRepository(TestContext.Current.CancellationToken));
    }
}
