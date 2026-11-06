using System.Data;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Application.Opinions;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class OpinionRepository(ApplicationDbContext context) : IOpinionRepository
{
    private sealed record OpinionRow(CandidateOpinion Entity, byte[] Version);
    private IQueryable<OpinionRow> OwnOpinion(Guid campaignId, Guid candidateId, OpinionSource source) =>
        context.CandidateOpinions.AsNoTracking()
            .Where(x => x.CampaignId == campaignId && x.CandidateEmployeeId == candidateId && x.OpinionSource == source)
            .Select(x => new OpinionRow(x, EF.Property<byte[]>(x, "RowVersion")));
    private static OpinionDto Dto(CandidateOpinion x, byte[] version) =>
        new(x.Id, x.CampaignId, x.CandidateEmployeeId, x.Content, x.CreatedAtUtc, x.LastModifiedAtUtc, Convert.ToBase64String(version));
    private Task<bool> Eligible(Guid campaignId, Guid candidateId, CancellationToken token) =>
        context.Preselections.AsNoTracking().AnyAsync(p => p.CampaignId == campaignId && p.CandidateEmployeeId == candidateId && p.IsSelected &&
            context.Nominations.Any(n => n.CampaignId == campaignId && n.CandidateEmployeeId == candidateId && !n.IsCancelled), token);

    public async Task<NominationResult<OpinionPage>> SearchAsync(Guid campaignId, OpinionSource source, string? term,
        int pageNumber, int pageSize, CancellationToken token)
    {
        try
        {
            var campaign = await context.Campaigns.AsNoTracking().Where(x => x.Id == campaignId)
                .Select(x => new { x.IsArchived }).SingleOrDefaultAsync(token);
            if (campaign is null) return new(null, NominationError.NotFound, "No encontramos la convocatoria.");
            var query = from p in context.Preselections.AsNoTracking()
                        join e in context.Employees.AsNoTracking() on p.CandidateEmployeeId equals e.Id
                        where !campaign.IsArchived && p.CampaignId == campaignId && p.IsSelected &&
                            context.Nominations.Any(n => n.CampaignId == campaignId && n.CandidateEmployeeId == e.Id && !n.IsCancelled)
                        select new { e.Id, e.PayrollNumber, e.FullName,
                            HasOpinion = context.CandidateOpinions.Any(o => o.CampaignId == campaignId && o.CandidateEmployeeId == e.Id && o.OpinionSource == source) };
            if (!string.IsNullOrWhiteSpace(term)) query = query.Where(x => x.PayrollNumber.Contains(term) || x.FullName.Contains(term));
            var total = await query.CountAsync(token);
            var items = await query.OrderBy(x => x.PayrollNumber).ThenBy(x => x.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .Select(x => new OpinionCandidate(x.Id, x.PayrollNumber, x.FullName, campaignId, x.HasOpinion)).ToArrayAsync(token);
            return new(new(items, total, pageNumber, pageSize));
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }

    public async Task<NominationResult<OpinionStatus>> GetAsync(Guid campaignId, Guid candidateId, OpinionSource source, CancellationToken token)
    {
        try
        {
            if (!await context.Campaigns.AsNoTracking().AnyAsync(x => x.Id == campaignId, token))
                return new(null, NominationError.NotFound, "No encontramos la convocatoria.");
            if (!await context.Employees.AsNoTracking().AnyAsync(x => x.Id == candidateId, token))
                return new(null, NominationError.NotFound, "No encontramos al candidato.");
            var row = await OwnOpinion(campaignId, candidateId, source).SingleOrDefaultAsync(token);
            return new(row is null ? new(false, null, "La opinión de tu departamento está pendiente.") : new(true, Dto(row.Entity, row.Version)));
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }

    public async Task<NominationResult<OpinionSaved>> SaveAsync(Guid campaignId, Guid candidateId, OpinionSource source, byte[]? version,
        Func<OpinionWriteContext, NominationResult<CandidateOpinion>> change, CancellationToken token, Guid? supervisorUserId = null)
    {
        CandidateOpinion? entity = null;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            if (!await CampaignAdministrationLock.AcquireAsync(context, token))
                throw new NominationPersistenceException(NominationError.Concurrency);
            if (source == OpinionSource.ImmediateSupervisor &&
                !await SupervisorRepository.Owned(context, supervisorUserId ?? Guid.Empty)
                    .AnyAsync(x => x.CampaignId == campaignId && x.CandidateEmployeeId == candidateId, token))
                return new(null, NominationError.Forbidden, "No eres el evaluador vinculado a este candidato.");
            var campaign = await context.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == campaignId, token);
            var candidateExists = await context.Employees.AsNoTracking().AnyAsync(x => x.Id == candidateId, token);
            var eligible = await Eligible(campaignId, candidateId, token);
            var row = await OwnOpinion(campaignId, candidateId, source).SingleOrDefaultAsync(token);
            if (row is null && version is not null || row is not null && (version is null || !row.Version.SequenceEqual(version)))
                return new(null, NominationError.Concurrency, "Consulta la opinión e incluye su versión actual antes de modificarla.");
            if (row is not null)
            {
                entity = row.Entity;
                var entry = context.CandidateOpinions.Attach(entity);
                entry.Property<byte[]>("RowVersion").CurrentValue = row.Version;
                entry.Property<byte[]>("RowVersion").OriginalValue = version!;
            }
            var result = change(new(campaign, candidateExists, eligible, entity));
            if (!result.IsSuccess) return new(null, result.Error, result.Message);
            entity = result.Value!;
            if (row is null) context.CandidateOpinions.Add(entity);
            context.ChangeTracker.DetectChanges();
            if (context.Entry(entity).State != EntityState.Unchanged) await context.SaveChangesAsync(token);
            var dto = Dto(entity, context.Entry(entity).Property<byte[]>("RowVersion").CurrentValue!);
            await transaction.CommitAsync(token);
            return new(new(dto, row is null));
        }
        catch (DbUpdateConcurrencyException ex) { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 1222 })
        { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        finally { if (entity is not null) context.Entry(entity).State = EntityState.Detached; }
    }
}
