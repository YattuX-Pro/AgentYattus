using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

public sealed class SettingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static UpdateInstanceSettingsRequest Request(InstanceSettingsDto s) => new(
        s.MaxIterations, s.MaxDurationMinutes, s.MaxConcurrentMissions, s.MaxFileKb, s.ExcludedFiles,
        s.AuditRetentionDays, s.MonthlyBudget, s.BudgetAlertPercent, s.BlockAtBudget);

    [Fact]
    public async Task Admins_read_and_change_the_settings_and_invalid_values_are_refused()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);

        var settings = (await root.GetFromJsonAsync<InstanceSettingsDto>(SettingsProtocol.Path, Ct))!;
        Assert.Equal((10, 15, 1, 90, true), (settings.MaxIterations, settings.MaxDurationMinutes, settings.MaxConcurrentMissions, settings.AuditRetentionDays, settings.BlockAtBudget));
        Assert.Contains(".env", settings.ExcludedFiles);
        Assert.False(settings.Environment.SensitiveTelemetry);

        var bad = Request(settings) with { MaxIterations = 0, ExcludedFiles = ["../secret"] };
        var refused = await root.PutAsJsonAsync(SettingsProtocol.Path, bad, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadAsStringAsync(Ct);
        Assert.Contains("maxIterations", problem);
        Assert.Contains("excludedFiles", problem);

        await root.PutAsJsonAsync(SettingsProtocol.Path, Request(settings) with { MaxIterations = 5, MonthlyBudget = 20m, ExcludedFiles = ["*.pem"] }, Ct);
        var changed = (await root.GetFromJsonAsync<InstanceSettingsDto>(SettingsProtocol.Path, Ct))!;
        Assert.Equal((5, 20m, "*.pem"), (changed.MaxIterations, changed.MonthlyBudget, Assert.Single(changed.ExcludedFiles)));

        var alice = As(api, "alice");
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync(SettingsProtocol.Path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsJsonAsync(SettingsProtocol.Path, Request(settings), Ct)).StatusCode);
    }

    [Fact]
    public async Task Missions_run_side_by_side_up_to_the_concurrency_setting()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var settings = (await root.GetFromJsonAsync<InstanceSettingsDto>(SettingsProtocol.Path, Ct))!;
        await root.PutAsJsonAsync(SettingsProtocol.Path, Request(settings) with { MaxConcurrentMissions = 2 }, Ct);

        var first = await root.CreateMissionAsync(FakeAgentRunner.LongGoal + " 1");
        var second = await root.CreateMissionAsync(FakeAgentRunner.LongGoal + " 2");
        var third = await root.CreateMissionAsync(FakeAgentRunner.LongGoal + " 3");

        await root.WaitForStatusAsync(first.Id, MissionStatus.Running);
        await root.WaitForStatusAsync(second.Id, MissionStatus.Running);
        await Task.Delay(200, Ct);
        // Deux places : la troisième attend son tour.
        Assert.Equal(MissionStatus.Pending, (await root.GetFromJsonAsync<MissionDto>($"/missions/{third.Id}", Ct))!.Status);
    }
}
