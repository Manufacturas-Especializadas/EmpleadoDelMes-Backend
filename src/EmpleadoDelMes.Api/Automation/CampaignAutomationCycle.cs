using EmpleadoDelMes.Application.Campaigns;

namespace EmpleadoDelMes.Api.Automation;

// One instance per worker. Its sequential caller prevents overlapping cycles.
public sealed class CampaignAutomationCycle(CampaignAutomationSettings settings, IServiceScopeFactory scopes,
    TimeProvider clock, ILogger logger)
{
    private DateOnly? completedLocalMonth;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled) return;
        cancellationToken.ThrowIfCancellationRequested();
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), settings.TimeZone!);
        var localMonth = new DateOnly(local.Year, local.Month, 1);
        if (completedLocalMonth == localMonth) return;
        if (settings.FirstEvaluationMonth >= localMonth)
        {
            completedLocalMonth = localMonth;
            return;
        }
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var campaigns = scope.ServiceProvider.GetRequiredService<CampaignService>();
            var checkedMonths = 0;
            for (var month = settings.FirstEvaluationMonth; month < localMonth; month = month.AddMonths(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Supply the following local month to reuse the previous-month use case unchanged.
                var result = await campaigns.EnsurePreviousMonthAsync(month.AddMonths(1), cancellationToken);
                if (!result.IsSuccess)
                {
                    logger.LogWarning("No se completó la generación mensual de convocatorias. Se reintentará en la próxima comprobación. Mes pendiente: {EvaluationMonth}", month);
                    return;
                }
                checkedMonths++;
            }
            completedLocalMonth = localMonth;
            logger.LogInformation("Comprobación mensual de convocatorias completada. Meses verificados: {Count}", checkedMonths);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Do not log exceptions/provider messages: they can expose connection configuration.
            logger.LogError("No se pudo comprobar la generación mensual de convocatorias. Se reintentará en la próxima comprobación.");
        }
    }
}
