using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Api.Tests;

public sealed class IdentityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Un client au nom d'une personne ; sans le rôle llm-admin, sauf demande.</summary>
    internal static HttpClient As(ApiFactory api, string user, bool instanceAdmin = false)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, instanceAdmin ? "llm-admin" : "membre");
        return client;
    }

    [Fact]
    public async Task Each_signed_in_person_enters_the_directory_and_sees_who_they_are()
    {
        await using var api = new ApiFactory();
        var alice = As(api, "alice");

        var me = (await alice.GetFromJsonAsync<MeDto>(IdentityProtocol.Me, Ct))!;

        Assert.Equal(("alice", "alice", false), (me.Id, me.Username, me.IsInstanceAdmin));
        Assert.Empty(me.Organizations);
        var users = (await As(api, "root", instanceAdmin: true).GetFromJsonAsync<UserDto[]>(IdentityProtocol.Users, Ct))!;
        Assert.Contains(users, u => u.Id == "alice");
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync(IdentityProtocol.Users, Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_instance_admins_create_organizations_with_a_valid_unique_slug()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);

        var created = await root.PostAsJsonAsync(IdentityProtocol.Organizations, new CreateOrganizationRequest("Studio Yattus", "studio-yattus"), Ct);
        var org = (await created.Content.ReadFromJsonAsync<OrganizationDto>(Ct))!;

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(OrganizationRole.Admin, Assert.Single(org.Members).Role);
        Assert.Equal(HttpStatusCode.Conflict, (await root.PostAsJsonAsync(IdentityProtocol.Organizations, new CreateOrganizationRequest("Autre", "studio-yattus"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PostAsJsonAsync(IdentityProtocol.Organizations, new CreateOrganizationRequest("Autre", "Pas Un Slug"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(api, "alice").PostAsJsonAsync(IdentityProtocol.Organizations, new CreateOrganizationRequest("A", "a-b"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Org_admins_manage_members_outsiders_see_nothing_and_the_last_admin_stays()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        var alice = As(api, "alice");
        var bob = As(api, "bob");
        await alice.GetAsync(IdentityProtocol.Me, Ct); // alice et bob entrent dans l'annuaire
        await bob.GetAsync(IdentityProtocol.Me, Ct);
        var org = (await (await root.PostAsJsonAsync(IdentityProtocol.Organizations, new CreateOrganizationRequest("Studio", "studio"), Ct))
            .Content.ReadFromJsonAsync<OrganizationDto>(Ct))!;

        Assert.Equal(HttpStatusCode.NoContent, (await root.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "alice"), new SetMemberRequest(OrganizationRole.Operator), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await root.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "inconnu"), new SetMemberRequest(OrganizationRole.Operator), Ct)).StatusCode);

        // alice voit l'organisation, bob non.
        Assert.Equal(OrganizationRole.Operator, Assert.Single((await alice.GetFromJsonAsync<OrganizationDto[]>(IdentityProtocol.Organizations, Ct))!).MyRole);
        Assert.Empty((await bob.GetFromJsonAsync<OrganizationDto[]>(IdentityProtocol.Organizations, Ct))!);
        // Un opérateur ne gère pas les membres (403) ; quelqu'un d'extérieur ne sait même pas qu'elle existe (404).
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "bob"), new SetMemberRequest(OrganizationRole.Observer), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "bob"), new SetMemberRequest(OrganizationRole.Admin), Ct)).StatusCode);
        // root est le seul administrateur : il ne peut ni se rétrograder ni partir.
        Assert.Equal(HttpStatusCode.BadRequest, (await root.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "root"), new SetMemberRequest(OrganizationRole.Observer), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await root.DeleteAsync(IdentityProtocol.Members(org.Id, "root"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Lookup_searches_colleagues_by_part_of_a_name_and_others_only_by_exact_username()
    {
        await using var api = new ApiFactory();
        var root = As(api, "root", instanceAdmin: true);
        await As(api, "aicha.diallo").GetAsync(IdentityProtocol.Me, Ct);
        var bob = As(api, "bob");
        await bob.GetAsync(IdentityProtocol.Me, Ct);

        // Sans organisation commune : seul le nom exact répond, on ne parcourt pas l'annuaire.
        Assert.Empty((await bob.GetFromJsonAsync<UserRefDto[]>($"{IdentityProtocol.Lookup}?q=dial", Ct))!);
        Assert.Equal("aicha.diallo", Assert.Single((await bob.GetFromJsonAsync<UserRefDto[]>($"{IdentityProtocol.Lookup}?q=Aicha.Diallo", Ct))!).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.GetAsync($"{IdentityProtocol.Lookup}?q=a", Ct)).StatusCode);

        // Collègues d'une même organisation : la recherche partielle marche.
        var org = (await (await root.PostAsJsonAsync(IdentityProtocol.Organizations, new CreateOrganizationRequest("Studio", "studio"), Ct))
            .Content.ReadFromJsonAsync<OrganizationDto>(Ct))!;
        await root.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "aicha.diallo"), new SetMemberRequest(OrganizationRole.Operator), Ct);
        await root.PutAsJsonAsync(IdentityProtocol.Members(org.Id, "bob"), new SetMemberRequest(OrganizationRole.Operator), Ct);
        Assert.Equal("aicha.diallo", Assert.Single((await bob.GetFromJsonAsync<UserRefDto[]>($"{IdentityProtocol.Lookup}?q=DIAL", Ct))!).Id);
    }
}
