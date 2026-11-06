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

public sealed class AuditExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("outil:read_file", "outil:read_file")]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("a,b", "\"a,b\"")]
    public void Cells_are_quoted_and_formulas_neutralized(string value, string expected) =>
        Assert.Equal(expected, AgentYattus.Api.Modules.Audit.AuditModule.Csv(value));

    [Fact]
    public async Task Admins_export_the_journal_as_csv()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Exportée");
        await client.WaitForFinalStatusAsync(mission.Id);

        var response = await client.GetAsync(AuditProtocol.Export, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("horodatage,mission,evenement", text.TrimStart('﻿'));
        Assert.Contains($"{mission.Id},Completed", text);
        var member = api.CreateClient();
        member.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "membre");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await member.GetAsync(AuditProtocol.Export, Ct)).StatusCode);
    }
}
