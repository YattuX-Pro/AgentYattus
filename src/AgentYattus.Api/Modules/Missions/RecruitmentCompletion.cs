using System.Text.Json;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AgentYattus.Api.Modules.Missions;

internal sealed class RecruitmentCompletion(MissionsDb db) : IRecruitmentCompletion
{
    public async Task<bool> CanCompleteAsync(Guid source, Guid origin, Guid project, string owner, CancellationToken ct)
    {
        var mission = await db.Missions.FindAsync([origin], ct);
        return mission is not null && mission.ProjectId == project && mission.CreatedBy == owner
            && mission.Kind == MissionKind.Recruitment && mission.Status == MissionStatus.Completed
            && mission.RecruitmentResult is { Draft: not null } result && (result.RequestId ?? mission.Id) == source;
    }

    public async Task CompleteAsync(Guid source, Guid origin, Guid project, string owner, Guid colleague, string name, CancellationToken ct)
    {
        var hr = await db.Missions.FindAsync([origin], ct);
        if (hr is null || hr.ProjectId != project || hr.CreatedBy != owner || hr.Kind != MissionKind.Recruitment
            || hr.Status != MissionStatus.Completed || hr.RecruitmentResult is not { } result || result.Created || (result.RequestId ?? hr.Id) != source) return;
        var message = $"{name} a rejoint l’équipe. Le recrutement est terminé.";
        var updatedResult = JsonSerializer.Serialize(result with { Message = message, Created = true }, JsonSerializerOptions.Web);
        var previousResult = hr.Result;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.Missions.Where(m => m.Id == origin && m.Result == previousResult)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Result, updatedResult), ct);
        if (updated == 0) return;
        hr.Result = updatedResult;
        var author = hr.Interlocutor ?? new MessageAuthorDto("_rh", "Agent RH");
        var turn = new MissionTurn { MissionId = origin, FromUser = false, AuthorJson = AuthorJson.Write(author), Text = message, At = DateTimeOffset.UtcNow };
        db.Turns.Add(turn);
        db.Notices.Add(new ConversationNotice { Id = Guid.CreateVersion7(), MissionId = origin, ProjectId = project, OwnerId = owner,
            Status = MissionStatus.Completed, AuthorJson = AuthorJson.Write(author), Text = message, At = turn.At, Turn = turn });
        var parent = await db.Missions.FirstOrDefaultAsync(m => m.RecruitmentMissionId == origin && m.ProjectId == project && m.CreatedBy == owner, ct);
        if (parent is not null)
        {
            var text = $"Le RH a terminé le recrutement : {name} a rejoint l’équipe.";
            db.Turns.Add(new MissionTurn { MissionId = parent.Id, FromUser = false, AuthorJson = parent.InterlocutorJson, Text = text, At = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
