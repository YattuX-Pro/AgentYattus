using System.Net;
using System.Net.Http.Json;
using AgentYattus.Contracts;

namespace AgentYattus.Client.Core.Tests;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task A_rejected_message_shows_the_servers_explanation_and_keeps_the_draft()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { errors = new { agent = new[] { "Cet agent n'est pas proposé." } } }),
        });
        var home = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") })) { Goal = "Hello Léon" };
        await home.LaunchMissionCommand.ExecuteAsync(null);
        Assert.Equal("Cet agent n'est pas proposé.", home.Error);
        Assert.Equal("Hello Léon", home.Goal);
        Assert.Empty(home.Missions);
    }

    [Fact]
    public void Navigating_away_and_back_closes_the_folder_picker_and_preserves_the_draft()
    {
        var api = new ApiClient(new HttpClient(new StubHandler(_ => throw new InvalidOperationException())));
        var home = new HomeViewModel(api) { Goal = "Mon brouillon" };
        var shell = new ShellViewModel(api, home);
        Assert.False(home.IsFolderOpen);
        home.IsFolderOpen = true;
        home.IsBrowsing = true;

        shell.Current = new ReportsViewModel(api);
        Assert.False(home.IsFolderOpen);
        Assert.False(home.IsBrowsing);
        shell.GoHomeCommand.Execute(null);
        Assert.Same(home, shell.Current);
        Assert.False(home.IsFolderOpen);
        Assert.Equal("Mon brouillon", home.Goal);
    }

    [Fact]
    public async Task Failed_attempts_stay_out_of_home_on_reload_and_live_updates()
    {
        var failed = new MissionDto(Guid.NewGuid(), "Essai", MissionStatus.Failed, DateTimeOffset.UtcNow);
        var completed = new MissionDto(Guid.NewGuid(), "Terminée", MissionStatus.Completed, DateTimeOffset.UtcNow);
        var running = new MissionDto(Guid.NewGuid(), "En cours", MissionStatus.Running, DateTimeOffset.UtcNow);
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath == "/missions"
                ? JsonContent.Create(new[] { failed, completed, running })
                : request.RequestUri.AbsolutePath == "/projects/workspace-roots"
                    ? JsonContent.Create(new WorkspaceRootsDto(true, []))
                    : request.RequestUri.AbsolutePath == "/projects" ? JsonContent.Create(Array.Empty<ProjectDto>())
                    : JsonContent.Create(Array.Empty<AgentDto>()),
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[] { completed.Id, running.Id }, vm.Missions.Select(m => m.Id));
        vm.Apply(new MissionEventDto(running.Id, MissionStatus.Failed, DateTimeOffset.UtcNow));
        Assert.Equal(completed.Id, Assert.Single(vm.Missions).Id);
    }

    [Fact]
    public void Launch_is_disabled_while_the_goal_is_blank()
    {
        var vm = new HomeViewModel(new ApiClient(new HttpClient(new StubHandler(_ => throw new InvalidOperationException()))));

        vm.Goal = "   ";

        Assert.False(vm.LaunchMissionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Launch_posts_the_goal_and_shows_the_created_mission()
    {
        var mission = new MissionDto(Guid.NewGuid(), "Relire le README", MissionStatus.Pending, DateTimeOffset.UtcNow);
        HttpRequestMessage? sent = null;
        var handler = new StubHandler(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(mission) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }))
        {
            Goal = "Relire le README",
        };

        MissionItem? opened = null;
        vm.MissionOpened += item => opened = item;
        await vm.LaunchMissionCommand.ExecuteAsync(null);

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("http://api/missions", sent.RequestUri!.ToString());
        Assert.Equal(mission.Id, Assert.Single(vm.Missions).Id);
        Assert.Same(vm.Missions[0], opened);
        Assert.Equal("", vm.Goal);

        vm.Apply(new MissionEventDto(mission.Id, MissionStatus.Completed, DateTimeOffset.UtcNow));

        Assert.True(vm.Missions[0].IsCompleted);
    }

    [Fact]
    public async Task Projects_where_one_can_launch_are_offered_and_the_chosen_one_is_sent()
    {
        var owner = new UserRefDto("u", "u", null);
        ProjectDto Project(string name, ProjectRole access, bool personal = false) => new(
            Guid.NewGuid(), name, null, null, owner, ProjectVisibility.Private, personal, null, access, false, [], DateTimeOffset.UtcNow);
        var personal = Project("Personnel", ProjectRole.Owner, personal: true);
        var shared = Project("API facturation", ProjectRole.Editor);
        var readOnly = Project("Lecture seule", ProjectRole.Reader);
        string? body = null;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new MissionDto(Guid.NewGuid(), "But", MissionStatus.Pending, DateTimeOffset.UtcNow)),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = path == "/projects" ? JsonContent.Create(new[] { readOnly, shared, personal }) : JsonContent.Create(Array.Empty<MissionDto>()),
            };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["API facturation", "Personnel"], vm.Projects.Select(p => p.Name));
        Assert.Equal(personal.Id, vm.SelectedProject!.Id);
        vm.SelectedProject = shared;
        vm.Goal = "But";
        await vm.LaunchMissionCommand.ExecuteAsync(null);
        Assert.Contains($"\"projectId\":\"{shared.Id}\"", body);
    }

    [Fact]
    public async Task A_question_is_launched_with_the_chosen_agent_and_a_review_keeps_its_circuit()
    {
        var bodies = new List<string>();
        AgentDto[] agents = [new("Analyste", "", "m", 10, [], "analyste"), new("Testeur", "", "m", 10, [], "testeur", Builtin: false)];
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new MissionDto(Guid.NewGuid(), "But", MissionStatus.Pending, DateTimeOffset.UtcNow)),
                };
            }
            var path = request.RequestUri!.AbsolutePath;
            object content = path == "/agents" ? agents : path == "/projects" ? Array.Empty<ProjectDto>() : Array.Empty<MissionDto>();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content, content.GetType()) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        // Par défaut, on parle au chef d'équipe (leçon 129) ; ici, pas d'équipe : le serveur confie la question à l'Analyste.
        // Un agent choisi répond en direct.
        Assert.Null(vm.SelectedAgent);
        Assert.Equal("Quelle mission pour l’équipe ?", vm.Title);
        vm.Goal = "Que fait ce dépôt ?";
        await vm.LaunchMissionCommand.ExecuteAsync(null);
        vm.SelectedAgent = vm.Agents[1];
        vm.Goal = "Vérifie les tests";
        await vm.LaunchMissionCommand.ExecuteAsync(null);
        vm.WriteReport = true;
        vm.Goal = "Revue";
        await vm.LaunchMissionCommand.ExecuteAsync(null);

        Assert.Contains("\"agent\":null", bodies[0]);
        Assert.Contains("\"agent\":\"testeur\"", bodies[1]);
        Assert.Contains("\"agent\":null", bodies[2]);
    }

    [Fact]
    public async Task A_develop_mission_carries_the_chosen_skills_and_an_imported_skill_is_read_then_validated()
    {
        var bodies = new List<string>();
        var statuses = new List<string>();
        SkillDto approved = new("conventions", "Conventions", "Pour toute modification de ce dépôt.", "1. Lire AGENTS.md.", true, false, 0, null, 2);
        SkillDto draft = new("brouillon", "Brouillon", "Une compétence pas encore validée.", "Corps.", true, false, 0, null, 1, SkillStatus.Draft);
        SkillDto imported = new("importee", "Importée", "Pour les revues de code de ce dépôt.", "1. Relire le diff ligne à ligne.", true, false, 0, null, 1,
            SkillStatus.Draft, [new SkillSourceDto("depot", "AGENTS.md")]);
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                statuses.Add(path + " " + request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Post && path == "/skills/import")
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new SkillImportDto(imported, ["Elle parle d'agir sans accord."])) };
            if (request.Method == HttpMethod.Post)
            {
                bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(new MissionDto(Guid.NewGuid(), "But", MissionStatus.Pending, DateTimeOffset.UtcNow)) };
            }
            object content = path switch
            {
                "/skills" => new[] { approved, draft },
                "/agents" => Array.Empty<AgentDto>(),
                "/projects" => Array.Empty<ProjectDto>(),
                _ => Array.Empty<MissionDto>(),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content, content.GetType()) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        // Un brouillon ne se choisit pas ; une compétence approuvée, avec sa version.
        Assert.Equal("Conventions · v2", Assert.Single(vm.MissionSkills).Label);
        vm.MissionSkills[0].IsSelected = true;
        Assert.Equal("Compétences · 1", vm.SkillsLabel);

        // Développer et REPORT.md s'excluent ; développer n'a pas de « à qui l'on parle ».
        vm.Develop = true;
        Assert.False(vm.IsQuestion);
        vm.WriteReport = true;
        Assert.False(vm.Develop);
        vm.Develop = true;
        Assert.False(vm.WriteReport);
        vm.Goal = "Ajoute un test pour OrderService";
        await vm.LaunchMissionCommand.ExecuteAsync(null);
        Assert.Contains("\"kind\":\"Develop\"", bodies[0]);
        Assert.Contains("\"skills\":[\"conventions\"]", bodies[0]);

        // Importer : le brouillon se relit en entier (procédure, références, avertissements, portée), puis se valide.
        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "---\nname: Importée\n---", TestContext.Current.CancellationToken);
        vm.PickFiles = () => Task.FromResult<IReadOnlyList<string>>([file]);
        await vm.ImportSkillCommand.ExecuteAsync(null);
        Assert.True(vm.HasPendingSkill);
        Assert.Contains("1. Relire le diff ligne à ligne.", vm.PendingSkillText);
        Assert.Contains("- depot : AGENTS.md", vm.PendingSkillText);
        Assert.Contains("sans accord", vm.PendingSkillText);
        Assert.Contains("Portée : personnelle", vm.PendingSkillText);
        await vm.ValidateSkillCommand.ExecuteAsync(null);
        Assert.Equal("/skills/importee/status {\"status\":\"Approved\"}", Assert.Single(statuses));
        Assert.False(vm.HasPendingSkill);
        Assert.Equal("Compétences · 2", vm.SkillsLabel);
        File.Delete(file);
    }

    [Fact]
    public async Task Launch_shows_an_error_when_the_api_fails()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }))
        {
            Goal = "Relire le README",
        };

        await vm.LaunchMissionCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Error);
        Assert.Empty(vm.Missions);
    }

    [Fact]
    public async Task Load_shows_the_missions_returned_by_the_api()
    {
        MissionDto[] missions =
        [
            new(Guid.NewGuid(), "Écrire le rapport", MissionStatus.AwaitingApproval, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "Relire le README", MissionStatus.Completed, DateTimeOffset.UtcNow, "Tout va bien."),
        ];
        var sent = new List<string>();
        var handler = new StubHandler(request =>
        {
            sent.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath == "/projects" ? JsonContent.Create(Array.Empty<ProjectDto>()) : JsonContent.Create(missions),
            };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["http://api/missions?limit=50", "http://api/projects", "http://api/projects/workspace-roots", "http://api/agents", "http://api/skills"], sent);
        Assert.Equal(missions.Select(m => m.Id), vm.Missions.Select(m => m.Id));
        Assert.True(vm.Missions[0].IsAwaitingApproval);
        Assert.Equal("À valider", vm.Missions[0].StatusLabel);
    }

    [Fact]
    public void Elapsed_time_is_short()
    {
        var now = new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal("en cours", MissionItem.Elapsed(true, now.AddDays(-3), now));
        Assert.Equal("2 min", MissionItem.Elapsed(false, now.AddMinutes(-2), now));
        Assert.Equal("hier", MissionItem.Elapsed(false, now.AddDays(-1), now));
    }

    [Fact]
    public async Task The_owner_browses_to_a_folder_in_the_app_without_climbing_above_the_allowed_root()
    {
        var project = new ProjectDto(Guid.NewGuid(), "Perso", null, null, new UserRefDto("me", "me", null), ProjectVisibility.Private, true, null,
            ProjectRole.Owner, true, [], DateTimeOffset.UtcNow);
        var root = Directory.CreateTempSubdirectory("racine").FullName;
        var folder = Directory.CreateDirectory(Path.Combine(root, "mon-depot")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        await File.WriteAllTextAsync(Path.Combine(folder, ".git", "HEAD"), "ref: refs/heads/main\n", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(root, ".cache"));
        string? put = null;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                put = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            object content = path switch
            {
                "/projects" => new[] { project },
                "/projects/workspace-roots" => new WorkspaceRootsDto(false, [root]),
                "/agents" => Array.Empty<AgentDto>(),
                _ => Array.Empty<MissionDto>(),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content, content.GetType()) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.CanChooseFolder);
        Assert.True(vm.NoFolder);
        vm.BrowseCommand.Execute(null);
        // Le dossier autorisé, sans les dossiers cachés ; un dépôt git montre sa branche.
        var entry = Assert.Single(vm.Entries);
        Assert.Equal(("mon-depot", "main"), (entry.Name, entry.Branch));
        vm.SelectCommand.Execute(entry);
        await vm.ChooseCommand.ExecuteAsync(null);

        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(folder), put);
        Assert.Equal(folder, vm.SelectedProject!.Workspace);
        Assert.Equal(("main", false), (vm.Branch, vm.IsFolderOpen));

        // Rouvert, il part d'à côté du dossier ; remonter au-dessus de la racine permise referme la navigation.
        vm.BrowseCommand.Execute(null);
        Assert.Equal(root, vm.BrowsePath);
        vm.UpCommand.Execute(null);
        Assert.False(vm.IsBrowsing);
    }

    [Fact]
    public async Task Attached_files_are_uploaded_then_listed_in_the_goal_for_read_file()
    {
        var project = new ProjectDto(Guid.NewGuid(), "Perso", null, null, new UserRefDto("me", "me", null), ProjectVisibility.Private, true, "/w",
            ProjectRole.Owner, true, [], DateTimeOffset.UtcNow);
        var local = Path.Combine(Directory.CreateTempSubdirectory().FullName, "devis.pdf");
        await File.WriteAllTextAsync(local, "%PDF", TestContext.Current.CancellationToken);
        string? goal = null;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/attachments"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new AttachmentDto("pieces-jointes/devis.pdf", "devis.pdf", 4)) };
            if (request.Method == HttpMethod.Post)
            {
                goal = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(new MissionDto(Guid.NewGuid(), "x", MissionStatus.Pending, DateTimeOffset.UtcNow)) };
            }
            object content = path switch
            {
                "/projects" => new[] { project },
                "/projects/workspace-roots" => new WorkspaceRootsDto(true, []),
                "/agents" => Array.Empty<AgentDto>(),
                _ => Array.Empty<MissionDto>(),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content, content.GetType()) };
        });
        var vm = new HomeViewModel(new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api/") }));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        await vm.AttachFilesAsync([local]);
        Assert.Equal("devis.pdf", Assert.Single(vm.Attachments).Name);
        vm.Goal = "Résume ce devis";
        await vm.LaunchMissionCommand.ExecuteAsync(null);

        var sent = System.Text.Json.JsonDocument.Parse(goal!).RootElement.GetProperty("goal").GetString();
        Assert.Equal("Résume ce devis\n\nPièces jointes (dans le dossier du projet, à lire avec read_file) :\n- pieces-jointes/devis.pdf", sent);
        Assert.Empty(vm.Attachments);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
