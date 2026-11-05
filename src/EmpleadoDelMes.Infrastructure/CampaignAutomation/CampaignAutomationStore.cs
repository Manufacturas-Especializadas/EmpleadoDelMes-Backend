using System.Data;
using EmpleadoDelMes.Application.CampaignAutomation;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.CampaignAutomation;

public sealed class CampaignAutomationStore(ApplicationDbContext context) : ICampaignAutomationStore
{
    public async Task<T> WithLockAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        try
        {
            await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            if (!await CampaignAdministrationLock.AcquireAsync(context, token)) throw Conflict();
            var result = await action();
            await tx.CommitAsync(token);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { throw Conflict(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 or 1222 }) { throw Conflict(); }
        catch (SqlException) { throw Storage(); }
        catch (DbUpdateException) { throw Storage(); }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task<CampaignAutomationConfiguration> ReadAsync(CancellationToken token)
    {
        try
        {
            return await context.Set<CampaignAutomationConfiguration>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == 1, token)
                ?? throw new AutomationException(CampaignError.Persistence, "Falta la configuración inicial. Verifica la migración antes de activar el generador.");
        }
        catch (SqlException) { throw Storage(); }
    }

    public async Task<IReadOnlyList<Campaign>> ReadCampaignsAsync(CancellationToken token)
    {
        try { return await context.Campaigns.AsNoTracking().ToArrayAsync(token); }
        catch (SqlException) { throw Storage(); }
    }

    public async Task SaveAsync(CampaignAutomationConfiguration configuration, byte[] expectedVersion,
        CampaignAutomationAuditEntry audit, CancellationToken token)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Se requiere una transacción administrativa.");
        context.Update(configuration);
        context.Entry(configuration).Property(c => c.RowVersion).OriginalValue = expectedVersion;
        context.Add(audit);
        await context.SaveChangesAsync(token);
    }

    private static AutomationException Conflict() => new(CampaignError.Concurrency, "Otra operación cambió la configuración o las convocatorias. Actualiza e inténtalo nuevamente.");
    private static AutomationException Storage() => new(CampaignError.Persistence, "No fue posible acceder a la configuración. El generador no puede continuar.");
}
