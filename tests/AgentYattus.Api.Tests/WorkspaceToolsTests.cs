using AgentYattus.Api.Modules.Agents;

namespace AgentYattus.Api.Tests;

/// <summary>La barrière du workspace face aux liens symboliques, qu'un dépôt cloné peut contenir.</summary>
public sealed class WorkspaceToolsTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-ws-").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("yattus-outside-").FullName;

    public WorkspaceToolsTests()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "contenu A");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(_workspace, "lien-dossier"), _outside);
        File.CreateSymbolicLink(Path.Combine(_workspace, "lien-fichier.txt"), Path.Combine(_outside, "secret.txt"));
    }

    public void Dispose()
    {
        Directory.Delete(_workspace, recursive: true);
        Directory.Delete(_outside, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("lien-fichier.txt")]
    [InlineData("lien-dossier/secret.txt")]
    public async Task ReadFile_refuses_paths_through_a_symbolic_link(string path)
    {
        var tools = new WorkspaceTools(_workspace);

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile(path, Ct));

        Assert.Contains("lien symbolique", error.Message);
    }

    [Fact]
    public void ListFiles_hides_symbolic_links_and_refuses_to_enter_them()
    {
        var tools = new WorkspaceTools(_workspace);

        Assert.Equal(new[] { "a.txt" }, tools.ListFiles("."));
        Assert.Throws<UnauthorizedAccessException>(() => tools.ListFiles("lien-dossier"));
    }

    [Fact]
    public void The_catalog_shown_to_users_matches_the_real_tools()
    {
        var tools = new WorkspaceTools(_workspace).AsAIFunctions();

        Assert.Equal(WorkspaceTools.Catalog.Select(t => (t.Name, t.RequiresApproval)),
            tools.Select(t => (t.Name, t is Microsoft.Extensions.AI.ApprovalRequiredAIFunction)));
    }

    [Fact]
    public async Task ReadFile_still_reads_regular_files()
    {
        var tools = new WorkspaceTools(_workspace);

        Assert.Equal("contenu A", await tools.ReadFile("a.txt", Ct));
    }

    [Fact]
    public async Task A_large_file_is_read_in_targeted_passages_with_a_correct_continuation()
    {
        File.WriteAllLines(Path.Combine(_workspace, "long.txt"), Enumerable.Range(1, 400).Select(i => $"ligne {i}"));
        var tools = new WorkspaceTools(_workspace);
        var first = await tools.ReadFile("long.txt", Ct);
        Assert.Contains("ligne 120\n", first);
        Assert.DoesNotContain("ligne 121\n", first);
        Assert.Contains("startLine=121", first);
        var next = await tools.ReadFile("long.txt", Ct, startLine: 121, lineCount: 2);
        Assert.Contains("ligne 121\nligne 122", next);
        Assert.DoesNotContain("ligne 123\n", next);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.ReadFile("long.txt", Ct, startLine: 0));
    }

    [Fact]
    public async Task Excluded_files_and_folders_are_invisible_and_refused()
    {
        File.WriteAllText(Path.Combine(_workspace, ".env"), "API_KEY=secret");
        Directory.CreateDirectory(Path.Combine(_workspace, "certs"));
        File.WriteAllText(Path.Combine(_workspace, "certs", "serveur.PFX"), "binaire");
        var tools = new WorkspaceTools(_workspace, excluded: [".env", "*.pfx"]);

        Assert.DoesNotContain(".env", tools.ListFiles());
        Assert.Empty(tools.ListFiles("certs"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile(".env", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile("certs/serveur.PFX", Ct)); // la casse ne compte pas
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.WriteFile(".env", "piège", Ct));
        Assert.Equal("contenu A", await tools.ReadFile("a.txt", Ct));

        // D'autres écritures du même fichier : casse, flux NTFS, nom court 8.3, « é » décomposé.
        File.WriteAllText(Path.Combine(_workspace, "clé.txt"), "secret");
        var accents = new WorkspaceTools(_workspace, excluded: ["clé.txt"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile(".ENV", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile(".env::$DATA", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile("ENV~1", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => accents.ReadFile("cle\u0301.txt", Ct));
        // Un caractère invisible (espace de largeur nulle) dans le nom : refusé.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile(".e\u200Bnv", Ct));
        // Le nom exact est exigé : « A.TXT » n'est pas « a.txt », même sur un disque qui les confond.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFile("A.TXT", Ct));
    }
}
