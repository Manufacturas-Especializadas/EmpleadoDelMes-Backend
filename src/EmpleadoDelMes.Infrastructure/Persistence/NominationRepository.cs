using System.Data;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class NominationRepository(ApplicationDbContext context, TimeProvider clock) : INominationRepository
{
    public async Task<NominationActor?> GetActorAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var user = await context.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.Id, u.EmployeeId, u.IsActive, u.MustChangePassword, u.LockoutEnabled, u.LockoutEnd,
                    IsSponsor = context.UserRoles.Any(ur => ur.UserId == u.Id && context.Roles.Any(r => r.Id == ur.RoleId && r.Name == AccountRoles.Sponsor))
                }).SingleOrDefaultAsync(cancellationToken);
            if (user is null) return null;
            var employee = user.EmployeeId is { } id
                ? await context.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken) : null;
            return new(user.Id, employee, user.IsActive, user.MustChangePassword, user.IsSponsor,
                user.LockoutEnabled && user.LockoutEnd > clock.GetUtcNow());
        }
        catch (SqlException ex) { throw Translate(ex); }
    }

    public async Task<IReadOnlyList<Campaign>> GetPotentialCampaignsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = clock.GetUtcNow();
            // Include future automatic openings: the domain evaluates acceptance at the later decision instant.
            return await context.Campaigns.AsNoTracking().Where(c => !c.IsArchived &&
                (c.NominationPeriod.Mode == CampaignPeriodMode.ManuallyOpen ||
                 c.NominationPeriod.Mode == CampaignPeriodMode.Automatic && c.NominationPeriod.ScheduledClosingAtUtc > now))
                .ToArrayAsync(cancellationToken);
        }
        catch (SqlException ex) { throw Translate(ex); }
    }

    public async Task<NominationResult<NominationConfirmation>> CreateAsync(Guid userId, Guid candidateId,
        Func<NominationCreationContext, NominationResult<Nomination>> create, CancellationToken cancellationToken)
    {
        Nomination? nomination = null;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            if (!await CampaignAdministrationLock.AcquireAsync(context, cancellationToken))
                throw new NominationPersistenceException(NominationError.Concurrency);
            var actor = await GetActorAsync(userId, cancellationToken);
            var candidate = await context.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == candidateId, cancellationToken);
            var campaigns = await GetPotentialCampaignsAsync(cancellationToken);
            var result = create(new(actor, candidate, campaigns));
            if (!result.IsSuccess) return new(null, result.Error, result.Message);
            nomination = result.Value!;
            context.Nominations.Add(nomination);
            // The historical unique index also covers cancelled nominations. No Exists-only protection.
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(NominationConfirmation.FromDomain(nomination));
        }
        catch (DbUpdateConcurrencyException ex) { throw new NominationPersistenceException(NominationError.Concurrency, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql) { throw Translate(sql); }
        catch (DbUpdateException ex) { throw new NominationPersistenceException(NominationError.Persistence, ex); }
        catch (SqlException ex) { throw Translate(ex); }
        finally
        {
            if (nomination is not null) context.Entry(nomination).State = EntityState.Detached;
        }
    }

    private static NominationPersistenceException Translate(SqlException exception) => new(exception.Number switch
    {
        2601 or 2627 => NominationError.Duplicate,
        1205 or 1222 or 547 => NominationError.Concurrency,
        _ => NominationError.Persistence
    }, exception);
}
