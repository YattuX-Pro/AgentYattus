using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Tests;

public sealed class MissionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Post_returns_202_and_the_mission_can_be_read()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/missions", new CreateMissionRequest("Relire le README"), Ct);
        var created = await response.Content.ReadFromJsonAsync<MissionDto>(Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/missions/{created!.Id}", response.Headers.Location?.ToString());
        var read = await client.GetFromJsonAsync<MissionDto>($"/missions/{created.Id}", Ct);
        Assert.Equal("Relire le README", read!.Goal);
    }

    [Fact]
    public async Task Post_with_empty_goal_returns_400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/missions", new CreateMissionRequest(" "), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_of_an_unknown_mission_returns_404()
    {
        var response = await factory.CreateClient().GetAsync($"/missions/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
