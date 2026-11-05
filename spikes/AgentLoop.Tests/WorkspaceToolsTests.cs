namespace AgentYattus.Spikes.Tests;

public sealed class WorkspaceToolsTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-").FullName;

    public WorkspaceToolsTests()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.txt"), "contenu A");
        Directory.CreateDirectory(Path.Combine(_workspace, "sub"));
        File.WriteAllText(Path.Combine(_workspace, "sub", "b.txt"), "contenu B");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    [Fact]
    public void ListFiles_returns_paths_relative_to_the_workspace()
    {
        var tools = new WorkspaceTools(_workspace);

        Assert.Equal(new[] { "a.txt", "sub" }, tools.ListFiles("."));
        Assert.Equal(new[] { Path.Combine("sub", "b.txt") }, tools.ListFiles("sub"));
    }

    [Fact]
    public async Task ReadFile_accepts_paths_that_stay_inside_the_workspace()
    {
        var tools = new WorkspaceTools(_workspace);

        var content = await tools.ReadFile("sub/../a.txt", TestContext.Current.CancellationToken);

        Assert.Equal("contenu A", content);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/../../outside.txt")]
    public async Task ReadFile_refuses_paths_outside_the_workspace(string path)
    {
        var tools = new WorkspaceTools(_workspace);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tools.ReadFile(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadFile_refuses_files_above_the_size_limit()
    {
        var tools = new WorkspaceTools(_workspace, maxFileBytes: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.ReadFile("a.txt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_refuses_a_missing_workspace()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new WorkspaceTools(Path.Combine(_workspace, "absent")));
    }
}
