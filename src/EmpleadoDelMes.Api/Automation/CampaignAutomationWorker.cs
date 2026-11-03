namespace EmpleadoDelMes.Api.Automation;

public sealed class CampaignAutomationWorker(IConfiguration configuration, IServiceScopeFactory scopes,
    TimeProvider clock, ILogger<CampaignAutomationWorker> logger) : BackgroundService
{
    private CampaignAutomationSettings settings = new(false);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Configuration errors fail startup before any SQL work; runtime storage failures do not.
        settings = CampaignAutomationSettings.Read(configuration);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled) return;
        var cycle = new CampaignAutomationCycle(settings, scopes, clock, logger);
        try
        {
            await cycle.CheckAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.CheckIntervalMinutes), clock);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await cycle.CheckAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
