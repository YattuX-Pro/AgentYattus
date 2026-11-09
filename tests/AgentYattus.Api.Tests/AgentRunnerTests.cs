using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.ScriptedChatClient;

namespace AgentYattus.Api.Tests;

/// <summary>Le vrai exécuteur, de bout en bout dans l'API, avec un faux modèle : gratuit et déterministe.</summary>
public sealed class AgentRunnerTests
{
    [Fact]
    public async Task A_mission_keeps_its_tokens_and_its_cost_at_the_catalog_price()
    {
        // Claude Haiku 5.5 au catalogue : 0,10 $ et 0,50 $ par million de tokens (entrée, sortie).
        var model = new ScriptedChatClient(Text("Réponse.", input: 1_000, output: 200));
        await using var api = new ScriptedModelApiFactory(model, modelName: "anthropic/claude-haiku-5-5");
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Combien ça coûte ?");
        await client.WaitForFinalStatusAsync(mission.Id);

        var done = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!;
        // 1 000 × 0,10 + 200 × 0,50 = 200 micro-dollars.
        Assert.Equal((1_000L, 200L, 0.0002m), (done.InputTokens, done.OutputTokens, done.Cost));
        Assert.Contains("usage:analyste:1000:200", await api.AuditOfAsync(mission.Id));
    }

    [Fact]
    public async Task Mission_runs_the_agent_and_audits_each_tool_call()
    {
        var model = new ScriptedChatClient(ToolCall("list_files", new() { ["path"] = "." }), Text("Un fichier : a.txt."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Que contient le workspace ?");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Equal("Un fichier : a.txt.", (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!.Result);
        Assert.Equal(["Running", "modele:claude-haiku-5-5", "cle:instance", "outil:list_files", "action:analyste:list_files:.", "usage:analyste:0:0", "Completed"], await api.AuditOfAsync(mission.Id));
        Assert.Equal("Que contient le workspace ?", model.Requests[0].Last().Text);
        Assert.Contains("a.txt", model.Requests[1].SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>()
            .Single().Result?.ToString());
    }

    [Fact]
    public async Task Reaching_the_iteration_limit_is_audited()
    {
        var model = new ScriptedChatClient(ToolCall("list_files", new() { ["path"] = "." }), Text("Réponse forcée."));
        await using var api = new ScriptedModelApiFactory(model, maxIterations: 1);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Explore tout");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Equal(["Running", "modele:claude-haiku-5-5", "cle:instance", "outil:list_files", "action:analyste:list_files:.", "limite-de-tours", "usage:analyste:0:0", "Completed"], await api.AuditOfAsync(mission.Id));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>L'équipe du projet personnel : Inès, l'Analyste, recrutée comme chef d'équipe (leçon 129), et Malik, le Rédacteur.</summary>
    private static async Task RecruitTeamAsync(HttpClient client)
    {
        var face = await TeamTests.PortraitAsync(client, "ines", "Inès");
        var project = (await client.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!.Single(p => p.IsPersonalDefault).Id;
        foreach (var recruit in new[] { new RecruitRequest("analyste", face.Id, "Inès", [], Leader: true), new RecruitRequest("redacteur", face.Id, "Malik", []) })
            (await client.PostAsJsonAsync(TeamProtocol.ProjectColleagues(project), recruit, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_leader_can_contact_the_builtin_hr_and_keep_his_conversation()
    {
        var model = new ScriptedChatClient(
            ToolCall("confier", new() { ["collegue"] = "_rh", ["consigne"] = "Préparer un analyste pour le siège libre." }),
            Text("Quel type d’analyses devra-t-il réaliser ?", input: 100, output: 20));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        await RecruitTeamAsync(client);
        var parent = await client.CreateMissionAsync("Il y a un siège libre : demande au RH de rajouter un analyste.", agent: "analyste");
        var done = await client.WaitForStatusAsync(parent.Id, MissionStatus.Completed);
        Assert.Equal(MissionKind.Question, done.Kind);
        Assert.NotNull(done.RecruitmentMissionId);
        Assert.Contains("agent RH", done.Result);
        var child = await client.WaitForStatusAsync(done.RecruitmentMissionId.Value, MissionStatus.Completed);
        Assert.Equal(MissionKind.Recruitment, child.Kind);
        Assert.Equal(done.ProjectId, child.ProjectId);
        Assert.Equal(done.CreatedBy, child.CreatedBy);
        Assert.Contains("siège libre", child.Goal);
        Assert.Contains("Quel type", child.Result);
        Assert.Contains("- _rh (Agent RH)", model.Options[0]!.Instructions);
        Assert.Contains("Ne cherche pas de contact RH", model.Options[0]!.Instructions);
        Assert.Contains(model.Options[1]!.Tools!, t => t.Name == "preparer_recrutement");
        Assert.DoesNotContain(model.Options[1]!.Tools!, t => t.Name is "read_file" or "write_file" or "confier");
        Assert.DoesNotContain((await client.GetFromJsonAsync<AgentDto[]>(AgentsProtocol.Path, Ct))!, a => !a.Builtin);
        Assert.Contains("delegue:_rh", await api.AuditOfAsync(parent.Id));
    }

    [Fact]
    public async Task The_leader_hands_the_request_to_a_colleague_who_answers_directly()
    {
        var model = new ScriptedChatClient(
            ToolCall("confier", new() { ["collegue"] = "redacteur", ["consigne"] = "Relis a.txt." }),
            ToolCall("read_file", new() { ["path"] = "a.txt" }, callId: "call_2"),
            Text("Il contient « contenu A »."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        await RecruitTeamAsync(client);

        var mission = await client.CreateMissionAsync("Que contient a.txt ?", agent: "analyste");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Equal("Il contient « contenu A ».", (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", Ct))!.Result);
        Assert.Contains("delegue:redacteur", await api.AuditOfAsync(mission.Id));
        // Le chef garde ses outils, reçoit « confier » et connaît son équipe, sans lui-même ;
        // la boucle s'arrête dès qu'il a passé la main (3 appels, pas 4), et le Rédacteur ne peut pas la repasser.
        Assert.Equal(["list_files", "read_file", "confier"], model.Options[0]!.Tools!.Select(t => t.Name).Where(t => t is not ("propose_skill" or "poser_question")));
        Assert.Contains("- redacteur (Rédacteur)", model.Options[0]!.Instructions);
        Assert.DoesNotContain("- analyste (Analyste)", model.Options[0]!.Instructions);
        Assert.Equal(3, model.Requests.Count);
        Assert.Equal("Que contient a.txt ?\n\nConsigne du chef d'équipe : Relis a.txt.", model.Requests[1].Last().Text);
        Assert.DoesNotContain("confier", model.Options[1]!.Tools!.Select(t => t.Name));
    }

    [Fact]
    public async Task The_leader_answers_himself_with_his_own_tools()
    {
        var model = new ScriptedChatClient(ToolCall("list_files", new() { ["path"] = "." }), Text("Un fichier : a.txt."));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        await RecruitTeamAsync(client);

        var mission = await client.CreateMissionAsync("Que contient le dossier ?", agent: "analyste");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Equal("Un fichier : a.txt.", (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", Ct))!.Result);
        var audit = await api.AuditOfAsync(mission.Id);
        Assert.Contains("usage:analyste:0:0", audit);
        Assert.DoesNotContain(audit, e => e.StartsWith("delegue:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_colleague_spoken_to_directly_cannot_hand_over_and_the_old_fixed_leader_is_gone()
    {
        var model = new ScriptedChatClient(Text("Bonjour !"));
        await using var api = new ScriptedModelApiFactory(model);
        var client = api.CreateClient();
        await RecruitTeamAsync(client);

        var mission = await client.CreateMissionAsync("Bonjour", agent: "redacteur");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.DoesNotContain("confier", model.Options[0]!.Tools!.Select(t => t.Name));
        // « chef » n'est plus un agent : seuls les collègues recrutés existent.
        var refused = await client.PostAsJsonAsync("/missions", new CreateMissionRequest("Bonjour", Agent: "chef"), Ct);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Mission_fails_without_an_api_key_and_says_how_to_fix_it()
    {
        await using var api = new ScriptedModelApiFactory(model: null);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Que contient le workspace ?");

        Assert.Equal(MissionStatus.Failed, await client.WaitForFinalStatusAsync(mission.Id));
        // La cause, que l'humain peut corriger, plutôt que « consultez les journaux du serveur » (invisibles en mode local).
        var result = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!.Result;
        Assert.StartsWith("Aucun modèle n'est connecté", result);
        Assert.Contains("Modèles et clés", result);
    }

    [Fact]
    public async Task Conversation_succeeds_without_a_workspace_and_has_no_file_tools()
    {
        var model = new ScriptedChatClient(Text("Bonjour !"));
        await using var api = new ScriptedModelApiFactory(model, withWorkspace: false);
        var client = api.CreateClient();

        var mission = await client.CreateMissionAsync("Bonjour");

        Assert.Equal(MissionStatus.Completed, await client.WaitForFinalStatusAsync(mission.Id));
        Assert.Single(model.Requests);
        Assert.DoesNotContain(model.Options.SelectMany(o => o?.Tools ?? []),
            t => t is Microsoft.Extensions.AI.AIFunction f && f.Name is "read_file" or "list_files" or "write_file" or "run_command" or "git_status");
        Assert.Contains("Aucun dossier de travail", model.Options[0]?.Instructions);
        Assert.Equal("Bonjour !", (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!.Result);
    }

    [Fact]
    public async Task Provider_rate_limit_is_explained_without_exposing_the_raw_error()
    {
        var model = new ScriptedChatClient(Text("jamais appelé"))
        {
            OnRequest = _ => throw new HttpRequestException("détail privé du fournisseur", null, System.Net.HttpStatusCode.TooManyRequests),
        };
        await using var api = new ScriptedModelApiFactory(model, withWorkspace: false);
        var client = api.CreateClient();
        var mission = await client.CreateMissionAsync("Bonjour");

        Assert.Equal(MissionStatus.Failed, await client.WaitForFinalStatusAsync(mission.Id));
        var done = (await client.GetFromJsonAsync<MissionDto>($"/missions/{mission.Id}", TestContext.Current.CancellationToken))!;
        Assert.Contains("429", done.Result);
        Assert.Contains("autre modèle", done.Result);
        Assert.DoesNotContain("détail privé", done.Result);
    }

    [Fact]
    public void The_action_event_keeps_only_a_clean_bounded_path()
    {
        string Event(object? path) => AgentYattus.Api.Modules.Agents.AgentFrameworkRunner.ActionEvent("analyste",
            new Microsoft.Extensions.AI.FunctionCallContent("c", "read_file", path is null ? null : new Dictionary<string, object?> { ["path"] = path }));

        Assert.Equal("action:analyste:read_file:src/Api.cs", Event("src/Api.cs"));
        // Un retour à la ligne ou un caractère invisible ne peut pas fabriquer une seconde ligne de journal.
        Assert.Equal("action:analyste:read_file:a.txtfaux", Event("a.txt\n\u200Bfaux"));
        Assert.Equal("action:analyste:read_file:.", Event(null));
        Assert.Equal(120, Event(new string('x', 500))["action:analyste:read_file:".Length..].Length);
        // Une moitié de paire isolée (que PostgreSQL refuserait) devient U+FFFD ; une coupe ne sépare jamais une paire.
        Assert.Equal("action:analyste:read_file:\uFFFDx", Event("\uD800x"));
        var cut = Event(new string('x', 119) + "😀")["action:analyste:read_file:".Length..];
        Assert.True(cut.EnumerateRunes().All(r => r.Value != 0xFFFD) && cut.EndsWith("😀"));
        // Un nom d'outil inventé par le modèle ne passe pas tel quel.
        Assert.Equal("?", AgentYattus.Api.Modules.Agents.AgentFrameworkRunner.ToolName("read_file:faux\nCompleted"));
    }
}
