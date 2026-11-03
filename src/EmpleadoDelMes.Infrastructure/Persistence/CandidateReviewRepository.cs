using EmpleadoDelMes.Application.Candidates;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class CandidateReviewRepository(ApplicationDbContext context) : ICandidateReviewRepository
{
    private IQueryable<Nomination> ValidNominations(Guid campaignId) => context.Nominations.AsNoTracking()
        .Where(n => n.CampaignId == campaignId && !n.IsCancelled);

    private IQueryable<Employee> SelectedEmployees(Guid campaignId) => context.Employees.AsNoTracking()
        .Where(e => context.Preselections.AsNoTracking().Any(p => p.CampaignId == campaignId && p.CandidateEmployeeId == e.Id && p.IsSelected));

    public async Task<NominationResult<CandidateReviewPage>> SearchAsync(NominationCandidateSearch search, CancellationToken token)
    {
        try
        {
            if (!await context.Campaigns.AsNoTracking().AnyAsync(c => c.Id == search.CampaignId, token))
                return new(null, NominationError.NotFound, "No encontramos la convocatoria.");
            var counts = ValidNominations(search.CampaignId).GroupBy(n => n.CandidateEmployeeId)
                .Select(g => new { CandidateId = g.Key, Count = g.Count() });
            var query = from employee in SelectedEmployees(search.CampaignId)
                        join count in counts on employee.Id equals count.CandidateId
                        select new { employee.Id, employee.PayrollNumber, employee.FullName, count.Count };
            if (!string.IsNullOrWhiteSpace(search.Term))
                query = query.Where(x => x.PayrollNumber.Contains(search.Term) || x.FullName.Contains(search.Term));
            var total = await query.CountAsync(token);
            var opinions = context.CandidateOpinions.AsNoTracking().Where(o => o.CampaignId == search.CampaignId);
            var items = await query.OrderByDescending(x => x.Count).ThenBy(x => x.PayrollNumber).ThenBy(x => x.Id)
                .Skip((search.PageNumber - 1) * search.PageSize).Take(search.PageSize)
                .Select(x => new CandidateReviewItem(search.CampaignId, x.Id, x.PayrollNumber, x.FullName, x.Count,
                    opinions.Any(o => o.CandidateEmployeeId == x.Id && o.OpinionSource == OpinionSource.HumanResources),
                    opinions.Any(o => o.CandidateEmployeeId == x.Id && o.OpinionSource == OpinionSource.Safety),
                    opinions.Any(o => o.CandidateEmployeeId == x.Id && o.OpinionSource == OpinionSource.Quality)))
                .ToArrayAsync(token);
            return new(new(items, total, search.PageNumber, search.PageSize));
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }

    public async Task<NominationResult<CandidateReviewDetail>> GetAsync(Guid campaignId, Guid candidateId, CancellationToken token)
    {
        try
        {
            var month = await context.Campaigns.AsNoTracking().Where(c => c.Id == campaignId)
                .Select(c => (DateOnly?)c.EvaluationMonth).SingleOrDefaultAsync(token);
            if (month is null) return new(null, NominationError.NotFound, "No encontramos la convocatoria.");
            var candidate = await SelectedEmployees(campaignId).Where(e => e.Id == candidateId)
                .Select(e => new { e.PayrollNumber, e.FullName }).SingleOrDefaultAsync(token);
            if (candidate is null)
                return new(null, NominationError.NotFound, "No encontramos un candidato elegible para esta revisión.");
            var nominations = await ValidNominations(campaignId).Where(n => n.CandidateEmployeeId == candidateId)
                .OrderByDescending(n => n.RegisteredAtUtc).ThenByDescending(n => n.Id)
                .Select(n => new ReviewNomination(n.Id, n.SponsorPayrollNumber, n.SponsorFullName,
                    n.Qualities, n.Justification, n.RegisteredAtUtc)).ToArrayAsync(token);
            if (nominations.Length == 0)
                return new(null, NominationError.NotFound, "El candidato ya no tiene postulaciones válidas para esta revisión.");
            var recorded = await context.CandidateOpinions.AsNoTracking()
                .Where(o => o.CampaignId == campaignId && o.CandidateEmployeeId == candidateId &&
                    (o.OpinionSource == OpinionSource.HumanResources || o.OpinionSource == OpinionSource.Safety || o.OpinionSource == OpinionSource.Quality))
                .Select(o => new ReviewOpinion(o.OpinionSource, true, o.Content, o.CreatedAtUtc, o.LastModifiedAtUtc))
                .ToArrayAsync(token);
            var opinions = new[] { OpinionSource.HumanResources, OpinionSource.Safety, OpinionSource.Quality }
                .Select(source => recorded.SingleOrDefault(o => o.OpinionSource == source)
                    ?? new ReviewOpinion(source, false, null, null, null)).ToArray();
            return new(new(campaignId, month.Value, candidateId, candidate.PayrollNumber, candidate.FullName,
                true, nominations.Length, nominations, opinions));
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }
}
