using System.Net;
using System.Net.Http.Json;
using AgentYattus.Api.Modules.Projects;
using AgentYattus.Contracts;
using static AgentYattus.Api.Tests.IdentityTests;

namespace AgentYattus.Api.Tests;

public sealed class ProjectsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ProjectDto> CreateAsync(HttpClient client, string name, Guid? org = null, ProjectVisibility? visibility = null)
    {
        var response = await client.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest(name, org, visibility), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Ct))!;
    }

    // Les projets partagés : chacun a aussi son projet personnel, créé à sa première visite (testé à part).
    private static async Task<ProjectDto[]> ListAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!.Where(p => !p.IsPersonalDefault)];

    [Fact]
    public async Task Everyone_has_a_personal_project_from_the_first_visit()
    {
        await using var api = new ApiFactory();
        var alice = As(api, "alice");

        var first = (await alice.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!;
        var again = (await alice.GetFromJsonAsync<ProjectDto[]>(ProjectsProtocol.Path, Ct))!;

        var personal = Assert.Single(first, p => p.IsPersonalDefault);
        Assert.Equal(personal.Id, Assert.Single(again, p => p.IsPersonalDefault).Id);
        Assert.Equal(ProjectRole.Owner, personal.MyAccess);
    }

    [Theory]
    // propriétaire, admin d'org, invité (projet partagé), invité (projet privé : ignoré), membre d'org (projet ouvert), admin d'instance, inconnu
    [InlineData("owner", null, ProjectVisibility.Private, false, ProjectRole.Owner)]
    [InlineData("x", OrganizationRole.Admin, ProjectVisibility.Private, false, ProjectRole.Editor)]
    [InlineData("guest", null, ProjectVisibility.Shared, false, ProjectRole.Editor)]
    [InlineData("guest", null, ProjectVisibility.Private, false, null)]
    [InlineData("x", OrganizationRole.Observer, ProjectVisibility.Organization, false, ProjectRole.Reader)]
    [InlineData("x", OrganizationRole.Operator, ProjectVisibility.Shared, false, null)]
    [InlineData("x", null, ProjectVisibility.Private, true, ProjectRole.Reader)]
    [InlineData("x", null, ProjectVisibility.Organization, false, null)]
    public void The_access_rule(string user, OrganizationRole? orgRole, ProjectVisibility visibility, bool instanceAdmin, ProjectRole? expected)
    {
        var project = new Project { Name = "p", OwnerId = "owner", OrganizationId = Guid.NewGuid(), Visibility = visibility };
        ProjectMember[] members = [new() { UserId = "guest", Role = ProjectRole.Editor, AddedBy = "owner" }];

        Assert.Equal(expected, ProjectAccess.Level(project, members, user, orgRole, instanceAdmin));
    }

    [Fact]
    public async Task A_private_project_is_invisible_until_shared_and_a_reader_cannot_manage_it()
    {
        await using var api = new ApiFactory();
        var alice = As(api, "alice");
        var bob = As(api, "bob");
        await bob.GetAsync(IdentityProtocol.Me, Ct);
        var project = await CreateAsync(alice, "Site vitrine");

        Assert.Equal((ProjectVisibility.Private, ProjectRole.Owner, true), (project.Visibility, project.MyAccess, project.CanManage));
        Assert.Empty(await ListAsync(bob));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"{ProjectsProtocol.Path}/{project.Id}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync(ProjectsProtocol.Members(project.Id, "bob"), new SetProjectMemberRequest(ProjectRole.Reader), Ct)).StatusCode);
        var seen = Assert.Single(await ListAsync(bob));
        Assert.Equal((ProjectVisibility.Shared, ProjectRole.Reader, false), (seen.Visibility, seen.MyAccess, seen.CanManage));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"{ProjectsProtocol.Path}/{project.Id}", new UpdateProjectRequest("Piraté", ProjectVisibility.Shared), Ct)).StatusCode);

        // Repasser en privé : l'invité perd l'accès, même s'il reste dans la liste.
        await alice.PutAsJsonAsync($"{ProjectsProtocol.Path}/{project.Id}", new UpdateProjectRequest("Site vitrine", ProjectVisibility.Private), Ct);
        Assert.Empty(await ListAsync(bob));
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(ProjectsProtocol.Members(project.Id, "alice"), new SetProjectMemberRequest(ProjectRole.Editor), Ct)).StatusCode);
    }

    [Fact]
    public async Task Organization_projects_follow_the_members_roles()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var (alice, bob, carol) = (As(api, "alice"), As(api, "bob"), As(api, "carol"));
        foreach (var c in new[] { alice, bob, carol }) await c.GetAsync(IdentityProtocol.Me, Ct);
        var org = (await (await root.PostAsJsonAsync(IdentityProtocol.Organizations, new CreateOrganizationRequest("Studio", "studio"), Ct))
            .Content.ReadFromJsonAsync<OrganizationDto>(Ct))!;
        await root.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "alice"), new SetMemberRequest(OrganizationRole.Operator), Ct);
        await root.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "bob"), new SetMemberRequest(OrganizationRole.Observer), Ct);

        var project = await CreateAsync(alice, "API facturation", org.Id, ProjectVisibility.Organization);

        Assert.Equal(("Studio", ProjectRole.Reader), (Assert.Single(await ListAsync(bob)).OrganizationName, Assert.Single(await ListAsync(bob)).MyAccess));
        Assert.Empty(await ListAsync(carol));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Projet", org.Id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Projet", org.Id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(ProjectsProtocol.Path, new CreateProjectRequest("Perso", null, ProjectVisibility.Organization), Ct)).StatusCode);
        // L'administrateur de l'organisation gère tous ses projets ; l'admin de l'instance lit tout, sans gérer.
        var asRoot = Assert.Single(await ListAsync(root));
        Assert.Equal((ProjectRole.Editor, true), (asRoot.MyAccess, asRoot.CanManage));
        Assert.Equal(project.Id, asRoot.Id);
    }

    [Fact]
    public async Task Owners_choose_a_workspace_inside_the_allowed_folders_and_admins_anywhere()
    {
        var allowed = Directory.CreateTempSubdirectory("autorise-").FullName;
        var repo = Directory.CreateDirectory(Path.Combine(allowed, "mon-depot")).FullName;
        var outside = Directory.CreateTempSubdirectory("dehors-").FullName;
        // Un lien dans le dossier autorisé vers un dossier hors de lui : refusé, comme le dossier lui-même.
        var link = Path.Combine(allowed, "raccourci");
        Directory.CreateSymbolicLink(link, outside);
        await using var api = new ApiFactory();
        api.Settings["Projects:AllowedRoots:0"] = allowed;
        var alice = As(api, "alice");
        var bob = As(api, "bob");
        var root = As(api, "root", instanceAdmin: true);
        var project = await CreateAsync(alice, "Agent Yattus");
        var folder = Directory.CreateTempSubdirectory().FullName;

        Assert.Equal(new WorkspaceRootsDto(false, [allowed]), await alice.GetFromJsonAsync<WorkspaceRootsDto>(ProjectsProtocol.WorkspaceRoots, Ct),
            new RootsComparer());
        Assert.True((await root.GetFromJsonAsync<WorkspaceRootsDto>(ProjectsProtocol.WorkspaceRoots, Ct))!.Anywhere);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(outside), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(link), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(repo), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(allowed), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(repo), Ct)).StatusCode);
        Assert.Equal(repo, Assert.Single(await ListAsync(alice)).Workspace);

        // Bob ne prend ni le dossier d'Alice, ni un dossier qui le contient ou qu'il contient.
        var his = await CreateAsync(bob, "Le sien");
        var inside = Directory.CreateDirectory(Path.Combine(repo, "src")).FullName;
        foreach (var taken in new[] { repo, inside })
            Assert.Equal(HttpStatusCode.BadRequest, (await bob.PutAsJsonAsync(ProjectsProtocol.Workspace(his.Id), new SetWorkspaceRequest(taken), Ct)).StatusCode);
        var own = Directory.CreateDirectory(Path.Combine(allowed, "depot-de-bob")).FullName;
        Assert.Equal(HttpStatusCode.NoContent, (await bob.PutAsJsonAsync(ProjectsProtocol.Workspace(his.Id), new SetWorkspaceRequest(own), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest("relatif/dossier"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(Path.Combine(folder, "absent")), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await root.PutAsJsonAsync(ProjectsProtocol.Workspace(project.Id), new SetWorkspaceRequest(folder), Ct)).StatusCode);

        var seen = Assert.Single(await ListAsync(alice));
        Assert.Equal(folder, seen.Workspace);
        var asRoot = (await ListAsync(root)).Single(p => p.Id == project.Id);
        Assert.Equal((ProjectRole.Reader, false), (asRoot.MyAccess, asRoot.CanManage));
    }

    private sealed class RootsComparer : IEqualityComparer<WorkspaceRootsDto?>
    {
        public bool Equals(WorkspaceRootsDto? x, WorkspaceRootsDto? y) => x?.Anywhere == y?.Anywhere && x!.Roots.SequenceEqual(y!.Roots);
        public int GetHashCode(WorkspaceRootsDto? obj) => 0;
    }
}
