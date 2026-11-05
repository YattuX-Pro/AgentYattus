using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Tests;

public sealed class AgentsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Agents_lists_both_roles_with_their_tools_and_flags_sensitive_ones()
    {
        var agents = await factory.CreateClient().GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, TestContext.Current.CancellationToken);

        Assert.Equal(["Analyste", "Rédacteur"], agents!.Select(a => a.Name));
        Assert.Equal(["list_files", "read_file"], agents![0].Tools.Select(t => t.Name));
        Assert.DoesNotContain(agents[0].Tools, t => t.RequiresApproval);
        Assert.Equal(["write_file"], agents[1].Tools.Where(t => t.RequiresApproval).Select(t => t.Name));
    }
}
