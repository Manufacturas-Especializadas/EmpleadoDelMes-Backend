
using EmpleadoDelMes.Application.Nominations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class NominationAdminRepository(
    ApplicationDbContext context) : INominationAdminRepository
{
    public async Task<NominationCandidatePage?> SearchCandidatesAsync(
        NominationCandidateSearch search, CancellationToken cancellationToken)
    {
        try
        {
            if (!await context.Campaigns.AsNoTracking().AnyAsync(x => x.Id == search.CampaignId, cancellationToken))
                return null;

            var counts = context.Nominations.AsNoTracking()
                .Where(x => x.CampaignId == search.CampaignId && !x.IsCancelled)
                .GroupBy(x => x.CandidateEmployeeId)
                .Select(group => new { CandidateEmployeeId = group.Key, ValidNominationsCount = group.Count() });

            var query = from count in counts
                        join employee in context.Employees.AsNoTracking() on count.CandidateEmployeeId equals employee.Id
                        join preselection in context.Preselections.AsNoTracking().Where(x => x.CampaignId == search.CampaignId)
                            on employee.Id equals preselection.CandidateEmployeeId into preselections
                        from preselection in preselections.DefaultIfEmpty()
                        select new { employee.Id, employee.PayrollNumber, employee.FullName, count.ValidNominationsCount,
                            IsPreselected = preselection != null && preselection.IsSelected,
                            Version = preselection == null ? null : EF.Property<byte[]>(preselection, "RowVersion") };

            if (!string.IsNullOrWhiteSpace(search.Term))
            {
                var term = search.Term;
                query = query.Where(x => x.PayrollNumber.Contains(term) || x.FullName.Contains(term));
            }

            // Count groups, not nominations. All aggregation, filtering and paging remain in SQL.
            var total = await query.CountAsync(cancellationToken);
            var rows = await query.OrderByDescending(x => x.ValidNominationsCount)
                .ThenBy(x => x.PayrollNumber).ThenBy(x => x.Id)
                .Skip((search.PageNumber - 1) * search.PageSize).Take(search.PageSize)
                .ToArrayAsync(cancellationToken);
            var items = rows.Select(x => new NominationCandidateDto(search.CampaignId, x.Id,
                x.PayrollNumber, x.FullName, x.ValidNominationsCount, x.IsPreselected,
                x.Version is null ? null : Convert.ToBase64String(x.Version))).ToArray();

            return new(items, total, search.PageNumber, search.PageSize);
        }
        catch (SqlException ex)
        {
            throw new NominationPersistenceException(NominationError.Persistence, ex);
        }
    }

    private IQueryable<DetailRow> DetailsQuery(Guid id) =>
        from nomination in context.Nominations.AsNoTracking()
        join campaign in context.Campaigns.AsNoTracking() on nomination.CampaignId equals campaign.Id
        where nomination.Id == id
        select new DetailRow(nomination, campaign.EvaluationMonth, campaign.IsArchived,
            EF.Property<byte[]>(nomination, "RowVersion"));

    private sealed record DetailRow(Nomination Nomination, DateOnly EvaluationMonth, bool IsArchived, byte[] Version);

    private static NominationAdminDetail ToDetail(Nomination n, DateOnly month, byte[] version) => new(
        new(n.Id, n.CampaignId, month, n.SponsorEmployeeId, n.SponsorPayrollNumber, n.SponsorFullName,
            n.CandidateEmployeeId, n.CandidatePayrollNumber, n.CandidateFullName, n.Qualities, n.RegisteredAtUtc, n.IsCancelled),
        n.CreatedByUserId, n.Justification, Convert.ToBase64String(version));

    public async Task<NominationAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var row = await DetailsQuery(id).SingleOrDefaultAsync(cancellationToken);
            return row is null ? null : ToDetail(row.Nomination, row.EvaluationMonth, row.Version);
        }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
    }

    public Task<NominationResult<NominationCorrectionResult>> CorrectAsync(Guid id, byte[] expectedVersion,
        Func<Nomination, bool, NominationResult<NominationAuditEntry>> correct, CancellationToken cancellationToken)
        => SaveAuditedChangeAsync(id, expectedVersion, correct, "La postulación se corrigió correctamente.", cancellationToken);

    public async Task<NominationResult<NominationAdminDetail>> CancelAsync(Guid id, byte[] expectedVersion,
        Func<Nomination, bool, NominationResult<NominationAuditEntry>> cancel, CancellationToken cancellationToken)
    {
        var result = await SaveAuditedChangeAsync(id, expectedVersion, cancel,
            "La postulación se anuló correctamente.", cancellationToken);
        return result.IsSuccess ? new(result.Value!.Detail) : new(null, result.Error, result.Message);
    }

    private async Task<NominationResult<NominationCorrectionResult>> SaveAuditedChangeAsync(Guid id, byte[] expectedVersion,
        Func<Nomination, bool, NominationResult<NominationAuditEntry>> change, string successMessage,
        CancellationToken cancellationToken)
    {
        Nomination? nomination = null;
        NominationAuditEntry? audit = null;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            // Same acquisition order as 5B/6A: prevents archive from racing with the administrative change.
            if (!await CampaignAdministrationLock.AcquireAsync(context, cancellationToken))
                throw new NominationPersistenceException(NominationError.Concurrency);
            var row = await DetailsQuery(id).SingleOrDefaultAsync(cancellationToken);
            if (row is null) return new(null, NominationError.NotFound, "No encontramos la postulación.");
            if (!row.Version.SequenceEqual(expectedVersion))
                throw new NominationPersistenceException(NominationError.Concurrency);

            nomination = row.Nomination;
            var entry = context.Nominations.Attach(nomination);
            entry.Property<byte[]>("RowVersion").CurrentValue = row.Version;
            entry.Property<byte[]>("RowVersion").OriginalValue = expectedVersion;
            var result = change(nomination, row.IsArchived);
            if (!result.IsSuccess) return new(null, result.Error, result.Message);
            audit = result.Value;
            if (audit is null)
                return new(new(ToDetail(nomination, row.EvaluationMonth, row.Version), false,
                    "No hay cambios que guardar. No se generó un registro de auditoría."));

            context.NominationAuditEntries.Add(audit);
            // Both UPDATE (including the original RowVersion predicate) and INSERT share this transaction.
            await context.SaveChangesAsync(cancellationToken);
            var detail = ToDetail(nomination, row.EvaluationMonth, entry.Property<byte[]>("RowVersion").CurrentValue!);
            await transaction.CommitAsync(cancellationToken);
            return new(new(detail, true, successMessage));
        }
        catch (DbUpdateConcurrencyException ex) { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 })
        { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        catch (SqlException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        finally
        {
            // Uncommitted transactions roll back on disposal. Discard only these attempted changes.
            if (audit is not null) context.Entry(audit).State = EntityState.Detached;
            if (nomination is not null) context.Entry(nomination).State = EntityState.Detached;
        }
    }

    public async Task<NominationAdminPage> SearchAsync(
        NominationAdminSearch search,
        CancellationToken cancellationToken)
    {
        try
        {
            // Consultar postulaciones y el mes de su convocatoria.
            // AsNoTracking evita seguimiento innecesario de EF Core.
            var query =
                from nomination in context.Nominations.AsNoTracking()
                join campaign in context.Campaigns.AsNoTracking()
                    on nomination.CampaignId equals campaign.Id
                select new
                {
                    Nomination = nomination,
                    campaign.EvaluationMonth
                };

            // Filtrar por convocatoria.
            if (search.CampaignId.HasValue)
            {
                query = query.Where(x =>
                    x.Nomination.CampaignId == search.CampaignId.Value);
            }

            // Filtrar por estado de anulación.
            if (search.IsCancelled.HasValue)
            {
                query = query.Where(x =>
                    x.Nomination.IsCancelled == search.IsCancelled.Value);
            }

            // Buscar por nombre o nómina del candidato o patrocinador.
            if (!string.IsNullOrWhiteSpace(search.Term))
            {
                var term = search.Term;

                query = query.Where(x =>
                    x.Nomination.CandidatePayrollNumber.Contains(term) ||
                    x.Nomination.CandidateFullName.Contains(term) ||
                    x.Nomination.SponsorPayrollNumber.Contains(term) ||
                    x.Nomination.SponsorFullName.Contains(term));
            }

            // Contar registros después de aplicar los filtros.
            var total = await query.CountAsync(cancellationToken);

            // Aplicar orden y paginación en SQL Server.
            var items = await query
                .OrderByDescending(x => x.Nomination.RegisteredAtUtc)
                .ThenByDescending(x => x.Nomination.Id)
                .Skip((search.PageNumber - 1) * search.PageSize)
                .Take(search.PageSize)
                .Select(x => new NominationAdminDto(
                    x.Nomination.Id,
                    x.Nomination.CampaignId,
                    x.EvaluationMonth,
                    x.Nomination.SponsorEmployeeId,
                    x.Nomination.SponsorPayrollNumber,
                    x.Nomination.SponsorFullName,
                    x.Nomination.CandidateEmployeeId,
                    x.Nomination.CandidatePayrollNumber,
                    x.Nomination.CandidateFullName,
                    x.Nomination.Qualities,
                    x.Nomination.RegisteredAtUtc,
                    x.Nomination.IsCancelled))
                .ToArrayAsync(cancellationToken);

            return new NominationAdminPage(
                items,
                total,
                search.PageNumber,
                search.PageSize);
        }
        catch (SqlException ex)
        {
            throw new NominationPersistenceException(
                NominationError.Persistence,
                ex);
        }
    }
}
