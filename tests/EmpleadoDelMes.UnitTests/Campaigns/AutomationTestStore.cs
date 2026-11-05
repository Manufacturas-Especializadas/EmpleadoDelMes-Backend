using EmpleadoDelMes.Application.CampaignAutomation;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace EmpleadoDelMes.UnitTests.Campaigns;

internal sealed class AutomationTestStore(CampaignTestRepository? campaigns = null) : ICampaignAutomationStore
{
    internal CampaignAutomationConfiguration Configuration = new();
    internal readonly List<CampaignAutomationAuditEntry> Audit = [];
    internal readonly SemaphoreSlim Gate = new(1);
    internal bool FailRead;
    private long version = 1;
    internal static AutomationCalendarInput Calendar => new(0, "08:00", 9, "18:00", 10, "08:00", 19, "18:00");
    internal void Initialize(bool enabled)
    {
        Configuration = new();
        if (enabled) Configuration.Configure(true, AutomationCalendarValidation.Parse(Calendar), Guid.NewGuid(), DateTimeOffset.UtcNow);
        SetVersion();
    }
    private void SetVersion() => typeof(CampaignAutomationConfiguration).GetProperty("RowVersion")!
        .SetValue(Configuration, BitConverter.GetBytes(version));

    public async Task<T> WithLockAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try { await Task.Yield(); return await action(); }
        finally { Gate.Release(); }
    }
    public Task<CampaignAutomationConfiguration> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (FailRead) throw new AutomationException(CampaignError.Persistence, "SQL no disponible.");
        return Task.FromResult(Configuration);
    }
    public Task<IReadOnlyList<Campaign>> ReadCampaignsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<Campaign>>(campaigns?.Campaigns.Values.ToArray() ?? []);
    public Task SaveAsync(CampaignAutomationConfiguration configuration, byte[] expectedVersion, CampaignAutomationAuditEntry audit, CancellationToken token)
    {
        if (!Configuration.RowVersion.SequenceEqual(expectedVersion)) throw new AutomationException(CampaignError.Concurrency, "Versión obsoleta.");
        Configuration = configuration; version++; SetVersion(); Audit.Add(audit);
        return Task.CompletedTask;
    }

    internal static void Register(IServiceCollection services, CampaignTestRepository? campaigns = null, bool enabled = true)
    {
        var store = new AutomationTestStore(campaigns); store.Initialize(enabled);
        services.AddSingleton<ICampaignAutomationStore>(store);
        services.AddSingleton(store);
        services.AddSingleton(new AutomationTechnicalSettings(true, TimeZoneInfo.FindSystemTimeZoneById("America/Monterrey"), new DateOnly(2026, 1, 1)));
        services.AddSingleton<CampaignAutomationStatus>();
        services.AddScoped<CampaignAutomationService>();
    }
}
