using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using Microsoft.Extensions.AI;
using static AgentYattus.Api.Tests.IdentityTests;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Répondre à l'agent après sa réponse : la mission reprend avec toute sa conversation (leçon 117).</summary>
public sealed class ConversationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reply_continues_the_same_conversation_and_every_turn_is_kept()
    {
        var model = new ScriptedChatClient(Text("Le README décrit l'API."), Text("Oui : trois routes, POST et deux GET."));
        await using var api = new ScriptedModelApiFactory(model);
        var root = As(api, "root", instanceAdmin: true);
        var mission = await root.CreateMissionAsync("Résume le README");
        await root.WaitForStatusAsync(mission.Id, MissionStatus.Completed);

        var replied = await root.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("Combien de routes ?"), Ct);
        Assert.Equal(HttpStatusCode.Accepted, replied.StatusCode);
        var done = await root.WaitForStatusAsync(mission.Id, MissionStatus.Completed);

        // Le modèle reçoit le but, sa première réponse, puis la question : la même conversation, pas une nouvelle.
        var second = model.Requests[1];
        Assert.Equal("Résume le README", second[0].Text);
        Assert.Contains(second, m => m.Role == ChatRole.Assistant && m.Text == "Le README décrit l'API.");
        Assert.Equal("Combien de routes ?", second[^1].Text);
        Assert.Equal("Oui : trois routes, POST et deux GET.", done.Result);
        var turns = (await root.GetFromJsonAsync<MissionTurnDto[]>($"/missions/{mission.Id}/turns", Ct))!;
        Assert.Equal([(false, "Le README décrit l'API."), (true, "Combien de routes ?"), (false, "Oui : trois routes, POST et deux GET.")],
            turns.Select(t => (t.FromUser, t.Text)));
    }

    [Fact]
    public async Task One_replies_only_to_a_finished_mission_that_kept_its_conversation_in_a_project_one_can_reach()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        // Le faux exécuteur ne rend pas de conversation : rien à continuer.
        var mission = await root.CreateMissionAsync("Résume le README");
        await root.WaitForStatusAsync(mission.Id, MissionStatus.Completed);

        Assert.Equal(HttpStatusCode.Conflict, (await root.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("Et ensuite ?"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest(" "), Ct)).StatusCode);
        // Alice n'a pas accès au projet : la mission n'existe pas pour elle.
        Assert.Equal(HttpStatusCode.NotFound, (await As(api, "alice").PostAsJsonAsync($"/missions/{mission.Id}/messages", new ReplyRequest("x"), Ct)).StatusCode);
    }
}
