using EmpleadoDelMes.Application.CampaignAutomation;

namespace EmpleadoDelMes.Api.Automation;

public sealed class CampaignAutomationCycle(CampaignAutomationSettings settings, IServiceScopeFactory scopes,
    TimeProvider clock, ILogger<CampaignAutomationWorker> logger)
{
    public async Task CheckAsync(CancellationToken token)
    {
        if (!settings.Enabled) return;
        token.ThrowIfCancellationRequested();
        await using var scope = scopes.CreateAsyncScope();
        var status = scope.ServiceProvider.GetRequiredService<CampaignAutomationStatus>();
        status.Checked(clock.GetUtcNow());
        if (settings.LegacyScheduleDetected)
        {
            status.Failed("Retira CampaignAutomation:Schedule:*; la generación está bloqueada.");
            logger.LogWarning("Generación bloqueada por configuración heredada CampaignAutomation:Schedule.");
            return;
        }
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), settings.TimeZone!);
        var localMonth = new DateOnly(local.Year, local.Month, 1);
        if (settings.FirstEvaluationMonth > localMonth) return;
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<CampaignAutomationService>();
            var created = 0;
            for (var month = settings.FirstEvaluationMonth; month <= localMonth; month = month.AddMonths(1))
            {
                token.ThrowIfCancellationRequested();
                var result = await service.GenerateAsync(month, token);
                if (!result.AdministrativelyEnabled)
                {
                    logger.LogInformation("Generación pausada por configuración administrativa.");
                    return;
                }
                if (result.Created)
                {
                    created++;
                    status.Generated(month, clock.GetUtcNow());
                }
            }
            logger.LogInformation("Comprobación mensual completada. Convocatorias creadas: {Count}.", created);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (AutomationException ex)
        {
            status.Failed(ex.Message);
            logger.LogWarning("Generación mensual detenida: {Reason}", ex.Message);
        }
        catch (Exception)
        {
            status.Failed("No fue posible completar la generación. Se reintentará en la siguiente comprobación.");
            logger.LogError("No fue posible completar la generación mensual. Se reintentará; revisa la configuración y la disponibilidad de SQL Server.");
        }
    }
}
