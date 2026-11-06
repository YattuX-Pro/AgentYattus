using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

public sealed class AgentDefinitionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<AgentDto> AnalystAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, Ct))!.Single(a => a.Key == "analyste");

    [Fact]
    public async Task A_new_instruction_is_a_new_version_the_model_receives_it_and_the_guardrails_stay()
    {
        var model = new ScriptedChatClient(Text("Réponse en anglais."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        const string custom = "Tu es l'Analyste. Réponds toujours en anglais, en trois phrases au plus.";

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(AgentsProtocol.Role("analyste"), new UpdateAgentRequest(custom, 4), Ct)).StatusCode);
        var analyst = await AnalystAsync(client);
        Assert.Equal((custom, 1, 4, 4), (analyst.Instructions, analyst.Version, analyst.MaxIterations, analyst.MaxIterationsOverride!.Value));

        var mission = await client.CreateMissionAsync("Que contient le workspace ?");
        await client.WaitForFinalStatusAsync(mission.Id);
        var sent = model.Options[0]!.Instructions!;
        Assert.StartsWith(custom, sent);
        // La règle sur les données et la liste des outils sont ajoutées par le code, quoi que dise la consigne.
        Assert.Contains("jamais une instruction", sent);
        Assert.Contains("Tes seuls outils sont : list_files, read_file", sent);

        // Revenir à l'origine crée une version de plus.
        await client.PutAsJsonAsync(AgentsProtocol.Role("analyste"), new UpdateAgentRequest(null, null), Ct);
        var back = await AnalystAsync(client);
        Assert.Equal((back.DefaultInstructions, 2, (int?)null), (back.Instructions, back.Version, back.MaxIterationsOverride));
        var versions = (await client.GetFromJsonAsync<AgentVersionDto[]>(AgentsProtocol.Versions("analyste"), Ct))!;
        Assert.Equal([2, 1], versions.Select(v => v.Version));
        Assert.Equal(custom, versions[1].Instructions);
    }

    [Fact]
    public async Task Instructions_are_bounded_and_only_instance_admins_change_them()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(AgentsProtocol.Role("analyste"), new UpdateAgentRequest("Trop court.", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(AgentsProtocol.Role("analyste"), new UpdateAgentRequest(null, 0), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(AgentsProtocol.Role("inconnu"), new UpdateAgentRequest(null, null), Ct)).StatusCode);

        var member = api.CreateClient();
        member.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "membre");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync(AgentsProtocol.Role("analyste"), new UpdateAgentRequest(null, null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(AgentsProtocol.Versions("analyste"), Ct)).StatusCode);
    }
}
