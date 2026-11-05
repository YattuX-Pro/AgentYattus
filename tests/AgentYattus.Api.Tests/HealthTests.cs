using System.Net;

namespace AgentYattus.Api.Tests;

public sealed class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_200()
    {
        var response = await factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
