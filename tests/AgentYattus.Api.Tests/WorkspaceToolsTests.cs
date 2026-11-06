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
    }
}
