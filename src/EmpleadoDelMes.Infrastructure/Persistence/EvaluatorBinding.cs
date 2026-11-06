using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Application.Nominations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace EmpleadoDelMes.Infrastructure.Persistence;

internal static class EvaluatorBinding
{
    // Caller holds CampaignAdministrationLock. Always acquire assignment lock second, never in reverse order.
    internal static async Task<CampaignEvaluator?> CaptureAsync(ApplicationDbContext db, Guid campaignId, Guid candidateId,
        DateTimeOffset now, CancellationToken token)
    {
        var existing = await db.CampaignEvaluators.AsNoTracking().SingleOrDefaultAsync(x => x.CampaignId == campaignId && x.CandidateEmployeeId == candidateId, token);
        if (existing is not null) return existing;
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        cmd.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'EmpleadoDelMes.SponsorAssignments.v1', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000; SELECT @r;";
        if ((int)(await cmd.ExecuteScalarAsync(token))! < 0) throw new NominationPersistenceException(NominationError.Concurrency);
        // Never attach an evaluator to an orphaned legacy opinion: that requires an explicit migration policy.
        if (await db.CandidateOpinions.AnyAsync(o => o.CampaignId == campaignId && o.CandidateEmployeeId == candidateId && o.OpinionSource == Domain.Enums.OpinionSource.ImmediateSupervisor, token))
            throw new NominationPersistenceException(NominationError.Concurrency);
        var assignment = await db.SponsorEmployeeAssignments.AsNoTracking().SingleOrDefaultAsync(a => a.EmployeeId == candidateId && a.IsActive, token);
        if (assignment is null) return null;
        var binding = new CampaignEvaluator(campaignId, candidateId, assignment.Id, now);
        db.CampaignEvaluators.Add(binding);
        return binding;
    }
}
