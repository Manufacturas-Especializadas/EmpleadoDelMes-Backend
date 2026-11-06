using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Application.Opinions;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class SupervisorRepository(ApplicationDbContext db, IOpinionRepository opinions, TimeProvider clock) : ISupervisorRepository
{
    internal static IQueryable<CampaignEvaluator> Owned(ApplicationDbContext db, Guid userId) =>
        db.CampaignEvaluators.AsNoTracking().Where(l => db.SponsorEmployeeAssignments.Any(a => a.Id == l.AssignmentId &&
            a.EmployeeId == l.CandidateEmployeeId && db.Users.Any(u => u.Id == userId && u.EmployeeId == a.SponsorEmployeeId && u.IsActive && !u.MustChangePassword &&
            db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AccountRoles.Sponsor)))));
    private IQueryable<CampaignEvaluator> Eligible(Guid user) => Owned(db, user).Where(l =>
        db.Preselections.Any(p => p.CampaignId == l.CampaignId && p.CandidateEmployeeId == l.CandidateEmployeeId && p.IsSelected) &&
        db.Nominations.Any(n => n.CampaignId == l.CampaignId && n.CandidateEmployeeId == l.CandidateEmployeeId && !n.IsCancelled));
    public async Task<SupervisorCampaignPage> CampaignsAsync(Guid userId, int page, int size, CancellationToken token)
    {
        try
        {
            var links = Eligible(userId);
            var query = db.Campaigns.AsNoTracking().Where(c => links.Any(l => l.CampaignId == c.Id));
            var total = await query.CountAsync(token);
            var items = await query.OrderByDescending(c => c.EvaluationMonth).ThenBy(c => c.Id).Skip((page - 1) * size).Take(size)
                .Select(c => new SupervisorCampaign(c.Id, c.EvaluationMonth, c.IsArchived)).ToArrayAsync(token);
            return new(items, total, page, size);
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }
    public async Task<OpinionPage> CandidatesAsync(Guid user, Guid campaign, string? term, bool? completed, int page, int size, CancellationToken token)
    {
        try
        {
            var query = from l in Eligible(user).Where(l => l.CampaignId == campaign)
                        join e in db.Employees.AsNoTracking() on l.CandidateEmployeeId equals e.Id
                        select new { e.Id, e.PayrollNumber, e.FullName, HasOpinion = db.CandidateOpinions.Any(o => o.CampaignId == campaign && o.CandidateEmployeeId == e.Id && o.OpinionSource == OpinionSource.ImmediateSupervisor) };
            if (!string.IsNullOrWhiteSpace(term)) query = query.Where(e => e.FullName.Contains(term) || e.PayrollNumber.Contains(term));
            if (completed.HasValue) query = query.Where(e => e.HasOpinion == completed.Value);
            var total = await query.CountAsync(token);
            var items = await query.OrderBy(e => e.PayrollNumber).ThenBy(e => e.Id).Skip((page - 1) * size).Take(size)
                .Select(e => new OpinionCandidate(e.Id, e.PayrollNumber, e.FullName, campaign, e.HasOpinion)).ToArrayAsync(token);
            return new(items, total, page, size);
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }
    public async Task<NominationResult<OpinionStatus>> GetAsync(Guid user, Guid campaign, Guid candidate, CancellationToken token)
    {
        try
        {
            if (!await Owned(db, user).AnyAsync(l => l.CampaignId == campaign && l.CandidateEmployeeId == candidate, token))
                return new(null, NominationError.Forbidden, "No eres el evaluador vinculado a este candidato.");
            return await opinions.GetAsync(campaign, candidate, OpinionSource.ImmediateSupervisor, token);
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }
    public async Task<NominationResult<EvaluatorLink>> LinkAsync(Guid campaign, Guid candidate, CancellationToken token)
    {
        CampaignEvaluator? link = null;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            if (!await CampaignAdministrationLock.AcquireAsync(db, token)) throw new NominationPersistenceException(NominationError.Concurrency);
            var state = await db.Campaigns.AsNoTracking().Where(c => c.Id == campaign).Select(c => new { c.IsArchived }).SingleOrDefaultAsync(token);
            if (state is null) return new(null, NominationError.NotFound, "No encontramos la convocatoria.");
            if (state.IsArchived) return new(null, NominationError.Concurrency, "No se pueden completar vinculaciones en una convocatoria archivada.");
            if (!await db.Preselections.AnyAsync(p => p.CampaignId == campaign && p.CandidateEmployeeId == candidate && p.IsSelected, token) ||
                !await db.Nominations.AnyAsync(n => n.CampaignId == campaign && n.CandidateEmployeeId == candidate && !n.IsCancelled, token))
                return new(null, NominationError.Concurrency, "El candidato debe estar preseleccionado y conservar una postulación válida.");
            link = await EvaluatorBinding.CaptureAsync(db, campaign, candidate, clock.GetUtcNow(), token);
            if (link is not null && db.Entry(link).State == EntityState.Added) await db.SaveChangesAsync(token);
            Guid? sponsor = link is null ? null : await db.SponsorEmployeeAssignments.Where(a => a.Id == link.AssignmentId).Select(a => (Guid?)a.SponsorEmployeeId).SingleAsync(token);
            await tx.CommitAsync(token);
            return new(new(campaign, candidate, sponsor, link is null));
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 1222 })
        { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        catch (SqlException ex) { throw new NominationPersistenceException(ex.Number is 1205 or 1222 ? NominationError.Concurrency : NominationError.Persistence, ex); }
        finally { if (link is not null) db.Entry(link).State = EntityState.Detached; }
    }
}
