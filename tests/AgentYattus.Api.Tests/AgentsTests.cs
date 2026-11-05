using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Tests;

public sealed class AgentsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Agents_lists_the_analyst_with_its_tools_and_flags_sensitive_ones()
    {
        var agents = await factory.CreateClient().GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, TestContext.Current.CancellationToken);

        var analyst = Assert.Single(agents!);
        Assert.Equal("Analyste", analyst.Name);
        Assert.Equal(["list_files", "read_file", "write_file"], analyst.Tools.Select(t => t.Name));
        Assert.Equal(["write_file"], analyst.Tools.Where(t => t.RequiresApproval).Select(t => t.Name));
    }
}
