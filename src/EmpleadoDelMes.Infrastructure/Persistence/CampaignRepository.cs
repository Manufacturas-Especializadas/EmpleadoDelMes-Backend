using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class CampaignRepository(ApplicationDbContext context, TimeProvider clock) : ICampaignRepository
{
    public Task<CampaignDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        ReadAsync(context.Campaigns.AsNoTracking().Where(c => c.Id == id), cancellationToken);

    public Task<CampaignDto?> GetByMonthAsync(DateOnly evaluationMonth, CancellationToken cancellationToken) =>
        ReadAsync(context.Campaigns.AsNoTracking().Where(c => c.EvaluationMonth == evaluationMonth), cancellationToken);

    private async Task<CampaignDto?> ReadAsync(IQueryable<Campaign> query, CancellationToken cancellationToken)
    {
        try
        {
            var row = await query.Select(c => new { Campaign = c, Version = EF.Property<byte[]>(c, "RowVersion") })
                .SingleOrDefaultAsync(cancellationToken);
            return row is null ? null : CampaignDto.FromDomain(row.Campaign, Convert.ToBase64String(row.Version), clock.GetUtcNow());
        }
        catch (SqlException ex) { throw new CampaignPersistenceException(CampaignError.Persistence, ex); }
    }

    public async Task<CampaignPage> ListAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            var query = context.Campaigns.AsNoTracking();
            var total = await query.CountAsync(cancellationToken);
            var campaigns = await query.OrderByDescending(c => c.EvaluationMonth).ThenByDescending(c => c.Id)
                .Skip(checked((pageNumber - 1) * pageSize)).Take(pageSize)
                .Select(c => new { Campaign = c, Version = EF.Property<byte[]>(c, "RowVersion") }).ToArrayAsync(cancellationToken);
            var now = clock.GetUtcNow();
            return new(campaigns.Select(r => CampaignDto.FromDomain(r.Campaign, Convert.ToBase64String(r.Version), now)).ToArray(), total, pageNumber, pageSize);
        }
        catch (SqlException ex) { throw new CampaignPersistenceException(CampaignError.Persistence, ex); }
    }

    public async Task<CampaignDto> CreateAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        var entry = context.Campaigns.Add(campaign);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            if (!await CampaignAdministrationLock.AcquireAsync(context, cancellationToken))
                throw new CampaignPersistenceException(CampaignError.Concurrency);
            if (await context.Campaigns.AsNoTracking().AnyAsync(c => c.EvaluationMonth == campaign.EvaluationMonth, cancellationToken))
                throw new CampaignPersistenceException(CampaignError.Duplicate);
            var others = await context.Campaigns.AsNoTracking()
                .Where(c => !c.IsArchived && c.NominationPeriod.Mode != Domain.Enums.CampaignPeriodMode.ManuallyClosed)
                .ToArrayAsync(cancellationToken);
            var now = clock.GetUtcNow();
            if (others.Any(other => CampaignNominationPolicy.Conflicts(campaign, other, now)))
                throw new CampaignPersistenceException(CampaignError.Concurrency);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CampaignDto.FromDomain(campaign, Convert.ToBase64String(entry.Property<byte[]>("RowVersion").CurrentValue!), clock.GetUtcNow());
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new CampaignPersistenceException(CampaignError.Duplicate, ex);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        { throw new CampaignPersistenceException(CampaignError.Concurrency, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 })
        { throw new CampaignPersistenceException(CampaignError.Concurrency, ex); }
        catch (DbUpdateException ex) { throw new CampaignPersistenceException(CampaignError.Persistence, ex); }
        catch (SqlException ex) { throw new CampaignPersistenceException(CampaignError.Persistence, ex); }
        finally
        {
            // Remove only this attempted insert so the winner can be queried after a collision.
            entry.State = EntityState.Detached;
        }
    }

    public async Task<CampaignResult<CampaignDto>> UpdateAsync(Guid id, byte[] expectedVersion,
        Func<Campaign, IReadOnlyList<Campaign>, CampaignResult<bool>> change, CancellationToken cancellationToken)
    {
        Campaign? campaign = null;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            if (!await CampaignAdministrationLock.AcquireAsync(context, cancellationToken))
                return new(null, CampaignError.Concurrency, "Otra operación está modificando las convocatorias. Inténtalo nuevamente.");

            // Read fresh values after obtaining the cross-instance lock, not from a previous tracked query.
            var row = await context.Campaigns.AsNoTracking().Where(c => c.Id == id)
                .Select(c => new { Campaign = c, Version = EF.Property<byte[]>(c, "RowVersion") }).SingleOrDefaultAsync(cancellationToken);
            if (row is null) return new(null, CampaignError.NotFound, "No encontramos la convocatoria.");
            if (!row.Version.SequenceEqual(expectedVersion)) return Conflict();
            campaign = row.Campaign;
            var entry = context.Campaigns.Attach(campaign);
            entry.Property<byte[]>("RowVersion").OriginalValue = expectedVersion;
            entry.Property<byte[]>("RowVersion").CurrentValue = row.Version;

            var others = await context.Campaigns.AsNoTracking()
                .Where(c => c.Id != id && !c.IsArchived && c.NominationPeriod.Mode != Domain.Enums.CampaignPeriodMode.ManuallyClosed)
                .ToArrayAsync(cancellationToken);
            var result = change(campaign, others);
            if (!result.IsSuccess) return new(null, result.Error, result.Message);
            await context.SaveChangesAsync(cancellationToken);
            var dto = CampaignDto.FromDomain(campaign, Convert.ToBase64String(entry.Property<byte[]>("RowVersion").CurrentValue!), clock.GetUtcNow());
            await transaction.CommitAsync(cancellationToken);
            return new(dto);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { return Conflict(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 }) { return Conflict(); }
        catch (DbUpdateException) { return StorageFailure(); }
        catch (SqlException) { return StorageFailure(); }
        finally
        {
            // Disposal rolls back every unsuccessful transaction; never keep rejected changes tracked.
            if (campaign is not null) context.Entry(campaign).State = EntityState.Detached;
        }
    }

    private static CampaignResult<CampaignDto> Conflict() => new(null, CampaignError.Concurrency,
        "La convocatoria cambió durante la operación. Vuelve a consultarla antes de guardar.");
    private static CampaignResult<CampaignDto> StorageFailure() => new(null, CampaignError.Persistence,
        "No pudimos guardar la convocatoria. Inténtalo nuevamente.");
}
