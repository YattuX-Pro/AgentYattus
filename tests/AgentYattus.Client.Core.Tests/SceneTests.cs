using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

/// <summary>Le journal d'une mission devient une suite de répliques, avec les textes réglés dans l'admin.</summary>
public sealed class SceneTests
{
    internal static ProjectTeamDto Team(params (string Role, string Name)[] colleagues) => new(
        Guid.NewGuid(),
        [.. colleagues.Select(c => new ColleagueDto(Guid.NewGuid(), Guid.Empty, c.Role, Guid.NewGuid(), c.Name, [], null, DateTimeOffset.UtcNow, true, null))],
        "Léon", null, null, 38, 1800,
        new Dictionary<string, string>
        {
            ["mission:debut"] = "On démarre : {objectif}",
            ["agent:analyste"] = "{analyste}, à toi.",
            ["outil:read_file"] = "Je lis {fichier}.",
            ["outil:list_files"] = "Je parcours {fichier}.",
            ["passage"] = "{redacteur}, je t'envoie mes constats.",
            ["approbation"] = "Je peux écrire {fichier}, {vous} ?",
            ["decision:approuvee"] = "Merci ! J'écris {fichier}.",
            ["mission:fin"] = "Terminé.",
        },
        CanRecruit: true);

    private static AuditEntryDto E(string type) => new(type, DateTimeOffset.UtcNow);

    [Fact]
    public void A_review_becomes_a_conversation_between_the_right_colleagues()
    {
        var events = new[]
        {
            E("Running"), E("agent:analyste"), E("modele:x"), E("outil:list_files"), E("action:analyste:list_files:."),
            E("outil:read_file"), E("action:analyste:read_file:src/Api.cs"), E("usage:analyste:1:2"),
            E("agent:redacteur"), E("approbation-demandee:write_file"), E("AwaitingApproval"),
            E("decision:approuvee"), E("Running"), E("outil:write_file"), E("action:redacteur:write_file:REPORT.md"), E("Completed"),
        };

        var lines = Scene.From(events, Team(("analyste", "Inès"), ("redacteur", "Malik")), "Revue de Orders.Api", "Yatt", "docs/REPORT.md");

        Assert.Equal(
            [
                ("chef", "On démarre : Revue de Orders.Api"),
                ("chef", "Inès, à toi."),
                ("analyste", "Je parcours le dossier."),
                ("analyste", "Je lis src/Api.cs."),
                ("analyste", "Malik, je t'envoie mes constats."),
                ("redacteur", "Je peux écrire REPORT.md, Yatt ?"),
                ("redacteur", "Merci ! J'écris REPORT.md."),
                ("chef", "Terminé."),
            ],
            lines.Select(l => (l.Speaker, l.Text)));
        // Les objets qui circulent : le dossier des archives, les constats, le brouillon.
        Assert.Equal(
            [SceneObject.Folder, SceneObject.Notes, SceneObject.Report],
            lines.Where(l => l.Carries != SceneObject.None).Select(l => l.Carries));
        Assert.Equal("attend votre accord", lines[5].Statuses["redacteur"]);
    }

    [Fact]
    public void An_event_without_a_line_is_silent_and_unknown_variables_stay_visible()
    {
        // « mission:echec » n'a pas de réplique dans cette équipe : rien n'est dit, rien ne plante.
        var lines = Scene.From([E("Running"), E("Failed")], Team(), "But", "Yatt");
        Assert.Single(lines);

        Assert.Equal("Bonjour {inconnu}, Yatt", Scene.Fill("Bonjour {inconnu}, {vous}", new Dictionary<string, string> { ["vous"] = "Yatt" }));
    }
}
