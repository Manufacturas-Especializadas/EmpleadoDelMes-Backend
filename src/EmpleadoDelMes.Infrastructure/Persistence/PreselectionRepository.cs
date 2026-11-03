using System.Data;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Application.Preselections;
using EmpleadoDelMes.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class PreselectionRepository(ApplicationDbContext context) : IPreselectionRepository
{
    public async Task<NominationResult<PreselectionDto>> ChangeAsync(Guid campaignId, Guid candidateId, byte[]? version,
        Func<PreselectionChangeContext, NominationResult<Preselection>> change, CancellationToken token)
    {
        Preselection? entity = null;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            if (!await CampaignAdministrationLock.AcquireAsync(context, token))
                throw new NominationPersistenceException(NominationError.Concurrency);
            var archived = await context.Campaigns.AsNoTracking().Where(x => x.Id == campaignId)
                .Select(x => (bool?)x.IsArchived).SingleOrDefaultAsync(token);
            var candidateExists = await context.Employees.AsNoTracking().AnyAsync(x => x.Id == candidateId, token);
            var eligible = await context.Nominations.AsNoTracking().AnyAsync(x =>
                x.CampaignId == campaignId && x.CandidateEmployeeId == candidateId && !x.IsCancelled, token);
            var row = await context.Preselections.AsNoTracking()
                .Where(x => x.CampaignId == campaignId && x.CandidateEmployeeId == candidateId)
                .Select(x => new { Entity = x, Version = EF.Property<byte[]>(x, "RowVersion") }).SingleOrDefaultAsync(token);
            if (version is not null && (row is null || !row.Version.SequenceEqual(version)))
                throw new NominationPersistenceException(NominationError.Concurrency);
            if (row is not null)
            {
                entity = row.Entity;
                var entry = context.Preselections.Attach(entity);
                entry.Property<byte[]>("RowVersion").CurrentValue = row.Version;
                entry.Property<byte[]>("RowVersion").OriginalValue = version ?? row.Version;
            }
            var result = change(new(archived, candidateExists, eligible, entity));
            if (!result.IsSuccess) return new(null, result.Error, result.Message);
            entity = result.Value!;
            if (row is null) context.Preselections.Add(entity);
            context.ChangeTracker.DetectChanges();
            if (context.Entry(entity).State != EntityState.Unchanged)
                await context.SaveChangesAsync(token);
            var currentVersion = context.Entry(entity).Property<byte[]>("RowVersion").CurrentValue!;
            var dto = new PreselectionDto(entity.CampaignId, entity.CandidateEmployeeId, entity.IsSelected,
                entity.LastChangedByUserId, entity.LastChangedAtUtc, Convert.ToBase64String(currentVersion));
            await transaction.CommitAsync(token);
            return new(dto);
        }
        catch (DbUpdateConcurrencyException ex) { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 1222 })
        { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        finally
        {
            if (entity is not null) context.Entry(entity).State = EntityState.Detached;
        }
    }
}
