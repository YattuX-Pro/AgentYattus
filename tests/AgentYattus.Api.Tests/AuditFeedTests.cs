using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Tests;

public sealed class AuditFeedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_feed_lists_every_mission_most_recent_first()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var first = await client.CreateMissionAsync("Première");
        await client.WaitForFinalStatusAsync(first.Id);
        var second = await client.CreateMissionAsync("Seconde");
        await client.WaitForFinalStatusAsync(second.Id);

        var feed = (await client.GetFromJsonAsync<AuditFeedEntryDto[]>(AuditProtocol.Path, Ct))!;

        Assert.Equal(second.Id, feed[0].MissionId);
        Assert.Contains(feed, e => e.MissionId == first.Id);
        Assert.Equal(2, (await client.GetFromJsonAsync<AuditFeedEntryDto[]>($"{AuditProtocol.Path}?limit=2", Ct))!.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{AuditProtocol.Path}?limit=501", Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_read_the_feed()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "observateur");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(AuditProtocol.Path, Ct)).StatusCode);
    }
}
