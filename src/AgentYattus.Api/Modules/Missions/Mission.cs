using System.Text.Json;
using AgentYattus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Missions;

internal sealed class Mission
{
    public Guid Id { get; set; }
    public required string Goal { get; set; }
    public MissionStatus Status { get; set; }
    public MissionKind Kind { get; set; }

    /// <summary>L'agent qui fait une mission « Question » (ADR 0013) ; null : l'Analyste.</summary>
    public string? Agent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Result { get; set; }
    public Guid? RecruitmentMissionId { get; set; }
    public string? InterlocutorJson { get; set; }
    public bool NeedsReply { get; set; }
    public bool ResultAccepted { get; set; }
    public Guid? ExecutionId { get; set; }
    public MessageAuthorDto? Interlocutor => AuthorJson.Read(InterlocutorJson);

    // À qui appartient la mission (ADR 0009). Null pour une mission antérieure aux projets.
    public Guid? ProjectId { get; set; }
    public string? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }

    // Ce qu'a consommé la mission, toutes exécutions confondues (ADR 0010). Coût en micro-dollars : un entier.
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CostMicros { get; set; }

    /// <summary>La part du coût payée par une clé personnelle (leçon 134) : elle ne compte pas dans le budget de l'instance.</summary>
    public long PersonalCostMicros { get; set; }

    /// <summary>La mission échouée ou arrêtée dont celle-ci est la relance.</summary>
    public Guid? RetryOf { get; set; }

    // Mission en pause : la conversation de l'agent (opaque pour ce module), ce qui attend une décision, et la décision.
    public string? Conversation { get; set; }
    public string? PendingApproval { get; set; }
    public Guid? ApprovalId { get; set; }
    public bool? Decision { get; set; }

    /// <summary>Ta réponse, en attente d'être donnée à l'agent (leçon 117) ; la conversation terminée reste dans Conversation.</summary>
    public string? FollowUp { get; set; }

    /// <summary>Demandée sur une branche à part (leçon 127).</summary>
    public bool OnBranch { get; set; }

    /// <summary>La branche créée pour la mission (« agent/3f2a1c9e »), et sa copie de travail.</summary>
    public string? Branch { get; set; }
    public string? Worktree { get; set; }

    /// <summary>Les compétences choisies par l'humain (leçon 132), séparées par des virgules.</summary>
    public string Skills { get; set; } = "";

    public IReadOnlyList<string> SkillKeys => Skills.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public RecruitmentResultDto? RecruitmentResult
    {
        get
        {
            if (Kind != MissionKind.Recruitment || Result is null)
                return null;
            try { return JsonSerializer.Deserialize<RecruitmentResultDto>(Result, JsonSerializerOptions.Web); }
            catch (JsonException) { return null; }
        }
    }

    public MissionDto ToDto(string? projectName = null, string? viewer = null) => new(
        Id, Goal, Status, CreatedAt, RecruitmentResult?.Message ?? Result,
        PendingApproval is null ? null : JsonSerializer.Deserialize<ApprovalDto>(PendingApproval, JsonSerializerOptions.Web),
        Kind, ProjectId, projectName, CreatedByName, InputTokens, OutputTokens, CostMicros / 1_000_000m, RetryOf, Agent, Branch,
        SkillKeys.Count == 0 ? null : SkillKeys, RecruitmentResult?.Draft, RecruitmentMissionId, Interlocutor, NeedsReply, RecruitmentResult?.Created ?? false, viewer is not null && CreatedBy == viewer && Conversation is not null && Status == MissionStatus.Completed, ResultAccepted, ExecutionId, RecruitmentResult?.RequestId);
}

/// <summary>Un tour de la conversation après le but : chaque réponse de l'agent, chaque message de l'humain.</summary>
internal sealed class MissionTurn
{
    public long Id { get; set; }
    public Guid MissionId { get; set; }
    public bool FromUser { get; set; }
    public string? AuthorJson { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset At { get; set; }
}

internal sealed class MissionsDb(DbContextOptions<MissionsDb> options) : DbContext(options)
{
    public const string Schema = "missions";

    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<MissionTurn> Turns => Set<MissionTurn>();
    public DbSet<ConversationNotice> Notices => Set<ConversationNotice>();
    public DbSet<ConversationDraft> Drafts => Set<ConversationDraft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Mission>().Property(m => m.Status).HasConversion<string>();
        modelBuilder.Entity<Mission>().Ignore(m => m.RecruitmentResult);
        modelBuilder.Entity<Mission>().Ignore(m => m.Interlocutor);
        modelBuilder.Entity<ConversationNotice>().HasIndex(n => new { n.OwnerId, n.ReadAt });
        modelBuilder.Entity<ConversationDraft>().HasKey(d => new { d.Scope, d.OwnerId });
        modelBuilder.Entity<Mission>().Property(m => m.Kind).HasConversion<string>().HasDefaultValue(MissionKind.Question);
        modelBuilder.Entity<Mission>().Property(m => m.CreatedBy).HasMaxLength(64);
        modelBuilder.Entity<Mission>().Property(m => m.CreatedByName).HasMaxLength(100);
        modelBuilder.Entity<Mission>().Property(m => m.Agent).HasMaxLength(40);
        modelBuilder.Entity<Mission>().Property(m => m.Branch).HasMaxLength(60);
        modelBuilder.Entity<Mission>().Property(m => m.Worktree).HasMaxLength(500);
        modelBuilder.Entity<Mission>().Property(m => m.Skills).HasMaxLength(220).HasDefaultValue("");
        modelBuilder.Entity<Mission>().HasIndex(m => m.ProjectId);
        modelBuilder.Entity<MissionTurn>().HasIndex(t => t.MissionId);
    }
}
