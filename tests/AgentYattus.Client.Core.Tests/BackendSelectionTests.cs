namespace AgentYattus.Client.Core.Tests;

public sealed class BackendSelectionTests
{
    [Fact]
    public void A_choice_is_remembered_but_never_opens_a_database_without_confirmation()
    {
        var folder = Directory.CreateTempSubdirectory("yattus-connection-").FullName;
        try
        {
            var path = Path.Combine(folder, "connection.json");
            new BackendSelection(false, "https://api.example/", "https://auth.example/realm", "https://admin.example/").Save(path);
            var vm = new BackendSelectionViewModel(path);
            BackendSelection? opened = null;
            vm.Selected += selection => opened = selection;
            Assert.Null(opened);
            Assert.True(vm.IsServer);
            vm.IsLocal = true;
            vm.ContinueCommand.Execute(null);
            Assert.True(opened!.IsLocal);
            Assert.True(BackendSelection.Load(path).IsLocal);
            Assert.Equal("https://api.example/", BackendSelection.Load(path).ApiUrl);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void An_invalid_server_cannot_replace_the_previous_choice()
    {
        var folder = Directory.CreateTempSubdirectory("yattus-connection-").FullName;
        try
        {
            var path = Path.Combine(folder, "connection.json");
            var previous = new BackendSelection(true, "http://localhost:5080/", "http://localhost:8380/realms/agent-yattus", "http://localhost:4200/");
            previous.Save(path);
            var vm = new BackendSelectionViewModel(path) { IsLocal = false, ApiUrl = "http://example.com/" };
            var opened = false;
            vm.Selected += _ => opened = true;
            vm.ContinueCommand.Execute(null);
            Assert.False(opened);
            Assert.NotNull(vm.Error);
            Assert.Equal(previous, BackendSelection.Load(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Startup_ignores_the_legacy_local_choice_and_keeps_the_server_addresses()
    {
        var folder = Directory.CreateTempSubdirectory("yattus-startup-").FullName;
        try
        {
            var path = Path.Combine(folder, "connection.json");
            new BackendSelection(true, "https://api.example/", "https://auth.example/realm", "https://admin.example/").Save(path);
            var startup = BackendSelection.ForStartup(path);
            Assert.Equal(BackendSelection.Defaults.IsLocal, startup.IsLocal);
            Assert.Equal(Environment.GetEnvironmentVariable("AGENTYATTUS_API_URL") ?? "https://api.example/", startup.ApiUrl);
            File.WriteAllText(path, "invalid json");
            Assert.Equal(BackendSelection.Defaults, BackendSelection.ForStartup(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("http://localhost:5080/", true)]
    [InlineData("https://example.com/api/", true)]
    [InlineData("http://example.com/", false)]
    [InlineData("https://user:password@example.com/", false)]
    [InlineData("https://example.com/?token=secret", false)]
    [InlineData("file:///tmp/database", false)]
    public void Addresses_are_validated_before_the_connection_is_saved(string address, bool valid) =>
        Assert.Equal(valid, BackendSelection.IsValidAddress(address));
}
