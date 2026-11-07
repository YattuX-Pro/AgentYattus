using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

/// <summary>Les fichiers glissés dans une mission sont copiés dans le dossier du projet (leçon 115).</summary>
public sealed class AttachmentsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MultipartFormDataContent File(string name, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(content), "file", name);
        return form;
    }

    [Fact]
    public async Task A_dropped_file_lands_in_the_project_folder_without_overwriting_or_escaping()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var alice = As(api, "alice");
        var bob = As(api, "bob");
        await bob.GetAsync(IdentityProtocol.Me, Ct);
        var project = (await (await alice.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Devis"), Ct)).Content.ReadFromJsonAsync<ProjectDto>(Ct))!;

        // Sans dossier, rien n'est copié.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsync(ProjectsProtocol.Attachments(project.Id), File("devis.pdf", "x"), Ct)).StatusCode);

        var folder = Directory.CreateTempSubdirectory("projet-").FullName;
        await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(folder), Ct);
        var first = (await (await alice.PostAsync(ProjectsProtocol.Attachments(project.Id), File("devis.pdf", "un"), Ct)).Content.ReadFromJsonAsync<AttachmentDto>(Ct))!;
        var second = (await (await alice.PostAsync(ProjectsProtocol.Attachments(project.Id), File("devis.pdf", "deux"), Ct)).Content.ReadFromJsonAsync<AttachmentDto>(Ct))!;
        var sneaky = (await (await alice.PostAsync(ProjectsProtocol.Attachments(project.Id), File("../../.env", "secret"), Ct)).Content.ReadFromJsonAsync<AttachmentDto>(Ct))!;

        Assert.Equal(("pieces-jointes/devis.pdf", "pieces-jointes/devis (2).pdf"), (first.Path, second.Path));
        Assert.Equal("un", await System.IO.File.ReadAllTextAsync(Path.Combine(folder, "pieces-jointes", "devis.pdf"), Ct));
        // Le nom seul, sans dossier ni point initial : ni évasion, ni fichier caché.
        Assert.Equal("pieces-jointes/env", sneaky.Path);
        Assert.False(System.IO.File.Exists(Path.Combine(folder, "..", ".env")));
        // Bob n'a pas accès au projet : 404.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync(ProjectsProtocol.Attachments(project.Id), File("x.txt", "x"), Ct)).StatusCode);
    }
}
