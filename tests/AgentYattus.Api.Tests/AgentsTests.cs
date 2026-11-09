using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Tests;

public sealed class AgentsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Agents_lists_both_roles_with_their_tools_and_flags_sensitive_ones()
    {
        var agents = await factory.CreateClient().GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, TestContext.Current.CancellationToken);

        // Le Développeur et le Relecteur (ADR 0016) suivent : ce sont les postes de la mission Développer.
        Assert.Equal(["Analyste", "Rédacteur", "Développeur", "Relecteur"], agents!.Select(a => a.Name));
        Assert.Equal(["list_files", "read_file"], agents![0].Tools.Select(t => t.Name));
        Assert.DoesNotContain(agents[0].Tools, t => t.RequiresApproval);
        Assert.Equal(["write_file"], agents[1].Tools.Where(t => t.RequiresApproval).Select(t => t.Name));
    }
}

public sealed class AgentStatsTests
{
    [Fact]
    public void Stats_count_runs_and_tokens_per_role_and_skip_malformed_events()
    {
        var stats = AgentYattus.Api.Modules.Agents.AgentsModule.Stats(
            ["usage:analyste:1000:200", "usage:analyste:500:100", "usage:redacteur:300:300", "usage:analyste:x:1", "usage:abimé"]);

        Assert.Equal(new AgentYattus.Contracts.AgentStatsDto(2, 1_800, 900), stats["analyste"]);
        Assert.Equal(new AgentYattus.Contracts.AgentStatsDto(1, 600, 600), stats["redacteur"]);
    }
}
