using System.Collections.Concurrent;
using EmpleadoDelMes.Api.Automation;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Campaigns;

[TestClass]
public sealed class CampaignAutomationTests
{
    // Timer callbacks are delivered only when the test advances the clock; no hour-long waits.
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        private readonly List<ManualTimer> timers = [];
        internal readonly TaskCompletionSource TimerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => current;
        internal void Advance(TimeSpan elapsed)
        {
            current += elapsed;
            ManualTimer[] active;
            lock (timers) active = timers.ToArray();
            foreach (var timer in active) timer.Tick(current);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (timers) timers.Add(timer);
            TimerCreated.TrySetResult();
            return timer;
        }
        private sealed class ManualTimer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset next;
            private TimeSpan interval;
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (disposed) return false;
                next = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.GetUtcNow() + dueTime;
                interval = period; return true;
            }
            internal void Tick(DateTimeOffset now)
            {
                if (disposed || now < next) return;
                next = interval == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now + interval;
                callback(state);
            }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class Log : ILogger<CampaignAutomationWorker>
    {
        internal readonly ConcurrentQueue<string> Messages = new();
        internal readonly SemaphoreSlim Events = new(0);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        void ILogger.Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.IsNull(exception); Messages.Enqueue(formatter(state, exception)); Events.Release();
        }
        internal async Task Next() => Assert.IsTrue(await Events.WaitAsync(TimeSpan.FromSeconds(10)), "El ciclo no terminó.");
    }

    private sealed class WaitingRepository : ICampaignRepository
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled;
        public async Task<CampaignDto?> GetByMonthAsync(DateOnly month, CancellationToken token)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
        public Task<CampaignDto?> GetByIdAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<CampaignDto> CreateAsync(Campaign campaign, CancellationToken token) => throw new NotSupportedException();
        public Task<CampaignPage> ListAsync(int page, int size, CancellationToken token) => throw new NotSupportedException();
        public Task<CampaignResult<CampaignDto>> UpdateAsync(Guid id, byte[] version,
            Func<Campaign, IReadOnlyList<Campaign>, CampaignResult<bool>> change, CancellationToken token) => throw new NotSupportedException();
    }

    [TestMethod]
    public async Task Worker_StartIsNotBlockedByStorage_AndStopCancelsInFlightRead()
    {
        var clock = new Clock(new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero)); var repository = new WaitingRepository();
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ICampaignRepository>(repository); services.AddScoped<CampaignService>();
        using var provider = services.BuildServiceProvider();
        using var worker = new CampaignAutomationWorker(Config(), provider.GetRequiredService<IServiceScopeFactory>(), clock, new Log());
        await worker.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await repository.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StopAsync(stop.Token); await worker.ExecuteTask!.WaitAsync(stop.Token);
        Assert.IsTrue(repository.Cancelled);
    }

    [TestMethod]
    public async Task UnexpectedFailure_IsSanitizedAndRetried()
    {
        var services = new ServiceCollection(); var attempts = 0;
        services.AddScoped<CampaignService>(_ => { attempts++; throw new InvalidOperationException("Sensitive configuration detail"); });
        using var provider = services.BuildServiceProvider(); var clock = new Clock(new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero));
        var log = new Log(); var cycle = Cycle(provider, clock, log: log);
        await cycle.CheckAsync(CancellationToken.None); await cycle.CheckAsync(CancellationToken.None);
        Assert.AreEqual(2, attempts); Assert.AreEqual(2, log.Messages.Count);
        Assert.IsFalse(string.Join(' ', log.Messages).Contains("Sensitive"));
    }

    private static IConfiguration Config(string? enabled = "true", string? zone = "America/Monterrey",
        string? first = "2026-10-01", string? interval = null) => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["CampaignAutomation:Enabled"] = enabled, ["CampaignAutomation:TimeZoneId"] = zone,
                ["CampaignAutomation:FirstEvaluationMonth"] = first, ["CampaignAutomation:CheckIntervalMinutes"] = interval
            }).Build();
    private static ServiceProvider Provider(CampaignTestRepository store, Clock clock)
    {
        store.Clock = clock;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock); services.AddSingleton<ICampaignRepository>(store); services.AddScoped<CampaignService>();
        return services.BuildServiceProvider();
    }
    private static CampaignAutomationCycle Cycle(ServiceProvider provider, Clock clock, IConfiguration? configuration = null, Log? log = null) =>
        new(CampaignAutomationSettings.Read(configuration ?? Config()), provider.GetRequiredService<IServiceScopeFactory>(), clock, log ?? new Log());

    [TestMethod]
    [DataRow(null)]
    [DataRow("false")]
    public async Task Disabled_DoesNotResolveCampaignServices(string? enabled)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var clock = new Clock(DateTimeOffset.MinValue);
        using var worker = new CampaignAutomationWorker(Config(enabled, "invalid", "invalid", "invalid"),
            provider.GetRequiredService<IServiceScopeFactory>(), clock, new Log());
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(clock.TimerCreated.Task.IsCompleted);
    }

    [TestMethod]
    [DataRow("invalid", "America/Monterrey", "2026-10-01", "60")]
    [DataRow("true", null, "2026-10-01", "60")]
    [DataRow("true", "Invalid/Zone", "2026-10-01", "60")]
    [DataRow("true", "Central Standard Time", "2026-10-01", "60")]
    [DataRow("true", "America/Monterrey", null, "60")]
    [DataRow("true", "America/Monterrey", "2026-10-02", "60")]
    [DataRow("true", "America/Monterrey", "01/10/2026", "60")]
    [DataRow("true", "America/Monterrey", "2026-10-01", "0")]
    [DataRow("true", "America/Monterrey", "2026-10-01", "1441")]
    public void InvalidConfiguration_IsRejectedBeforeStarting(string enabled, string? zone, string? first, string interval) =>
        Assert.Throws<InvalidOperationException>(() => CampaignAutomationSettings.Read(Config(enabled, zone, first, interval)));

    [TestMethod]
    [DataRow(2026, 11, 2026, 10)]
    [DataRow(2027, 1, 2026, 12)]
    public async Task LocalMonth_CreatesPreviousMonthClosed(int year, int month, int expectedYear, int expectedMonth)
    {
        var clock = new Clock(new(year, month, 1, 18, 0, 0, TimeSpan.Zero)); var store = new CampaignTestRepository();
        using var provider = Provider(store, clock);
        var expected = new DateOnly(expectedYear, expectedMonth, 1);
        await Cycle(provider, clock, Config(first: expected.ToString("yyyy-MM-dd"))).CheckAsync(CancellationToken.None);
        var campaign = store.Campaigns.Values.Single();
        Assert.AreEqual(expected, campaign.EvaluationMonth); Assert.AreEqual(clock.GetUtcNow(), campaign.CreatedAtUtc);
        Assert.IsFalse(campaign.IsArchived);
        Assert.AreEqual(CampaignPeriodMode.ManuallyClosed, campaign.NominationPeriod.Mode);
        Assert.AreEqual(CampaignPeriodMode.ManuallyClosed, campaign.OpinionPeriod.Mode);
        Assert.IsNull(campaign.NominationPeriod.ScheduledOpeningAtUtc); Assert.IsNull(campaign.OpinionPeriod.ScheduledClosingAtUtc);
    }

    [TestMethod]
    public async Task UtcMonthBoundary_UsesLocalDate_AndNewLocalMonthTriggersCheck()
    {
        var clock = new Clock(new(2026, 11, 1, 0, 30, 0, TimeSpan.Zero)); var store = new CampaignTestRepository();
        using var provider = Provider(store, clock); var cycle = Cycle(provider, clock, Config(first: "2026-09-01"));
        await cycle.CheckAsync(CancellationToken.None);
        Assert.IsTrue(store.Campaigns.ContainsKey(new(2026, 9, 1))); Assert.AreEqual(1, store.Campaigns.Count);
        var reads = store.MonthReads;
        await cycle.CheckAsync(CancellationToken.None); Assert.AreEqual(reads, store.MonthReads);
        clock.Advance(TimeSpan.FromHours(6)); await cycle.CheckAsync(CancellationToken.None);
        Assert.IsTrue(store.Campaigns.ContainsKey(new(2026, 10, 1))); Assert.AreEqual(2, store.Campaigns.Count);
    }

    [TestMethod]
    public async Task RecoveryAndRestart_PreserveExistingAndRespectFirstMonth()
    {
        var clock = new Clock(new(2027, 2, 5, 18, 0, 0, TimeSpan.Zero)); var store = new CampaignTestRepository();
        var existing = new Campaign(Guid.NewGuid(), new(2026, 11, 1), clock.GetUtcNow().AddDays(-70));
        existing.ScheduleNominations(clock.GetUtcNow(), clock.GetUtcNow().AddDays(1)); existing.OpenNominations();
        existing.ScheduleOpinions(clock.GetUtcNow(), clock.GetUtcNow().AddDays(2)); existing.Archive();
        store.Campaigns.Add(existing.EvaluationMonth, existing);
        using var provider = Provider(store, clock); var before = store.ToDto(existing);
        var cycle = Cycle(provider, clock);
        await cycle.CheckAsync(CancellationToken.None); await cycle.CheckAsync(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 1), new(2026, 11, 1), new(2026, 12, 1), new(2027, 1, 1) }, store.Campaigns.Keys.Order().ToArray());
        Assert.AreEqual(before, store.ToDto(existing)); Assert.AreEqual(3, store.CreateCalls);
        var reads = store.MonthReads;
        await Cycle(provider, clock).CheckAsync(CancellationToken.None); // New instance has no in-memory completion mark.
        Assert.IsTrue(store.MonthReads > reads); Assert.AreEqual(3, store.CreateCalls);
    }

    [TestMethod]
    public async Task PartialFailure_RetriesMissingMonthsWithoutDuplicatingCompletedOnes()
    {
        var clock = new Clock(new(2027, 1, 5, 18, 0, 0, TimeSpan.Zero));
        var store = new CampaignTestRepository { Failure = CampaignError.Persistence, FailOnMonth = new(2026, 11, 1) };
        using var provider = Provider(store, clock); var log = new Log(); var cycle = Cycle(provider, clock, log: log);
        await cycle.CheckAsync(CancellationToken.None); Assert.AreEqual(1, store.Campaigns.Count);
        store.Failure = null; await cycle.CheckAsync(CancellationToken.None);
        Assert.AreEqual(3, store.Campaigns.Count); Assert.AreEqual(0, store.DuplicateWrites);
        Assert.IsFalse(string.Join(' ', log.Messages).Contains("Internal database detail"));
    }

    [TestMethod]
    public async Task TwoInstances_ReuseUniqueMonthRecovery()
    {
        var clock = new Clock(new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero)); var store = new CampaignTestRepository { CoordinateTwoReads = true };
        using var provider = Provider(store, clock); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(Cycle(provider, clock).CheckAsync(timeout.Token), Cycle(provider, clock).CheckAsync(timeout.Token));
        Assert.AreEqual(1, store.Campaigns.Count); Assert.AreEqual(1, store.DuplicateWrites);
    }

    [TestMethod]
    public async Task FutureFirstMonth_DoesNotResolvePersistence()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var clock = new Clock(new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero));
        await Cycle(provider, clock, Config(first: "2026-11-01")).CheckAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Worker_ImmediateCheck_TimerRetry_AndCancellation_UseControlledTime()
    {
        var clock = new Clock(new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero)); var store = new CampaignTestRepository { Failure = CampaignError.Persistence };
        using var provider = Provider(store, clock); var log = new Log();
        using var worker = new CampaignAutomationWorker(Config(), provider.GetRequiredService<IServiceScopeFactory>(), clock, log);
        await worker.StartAsync(CancellationToken.None);
        await log.Next(); Assert.AreEqual(0, store.Campaigns.Count);
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        store.Failure = null; clock.Advance(TimeSpan.FromMinutes(60));
        await log.Next(); Assert.AreEqual(1, store.Campaigns.Count);
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StopAsync(stopTimeout.Token);
        await worker.ExecuteTask!.WaitAsync(stopTimeout.Token);
        var reads = store.MonthReads; clock.Advance(TimeSpan.FromHours(2)); Assert.AreEqual(reads, store.MonthReads);
    }

    [TestMethod]
    public async Task Cancellation_PreventsDatabaseWork()
    {
        var clock = new Clock(new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero)); var store = new CampaignTestRepository();
        using var provider = Provider(store, clock); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Cycle(provider, clock).CheckAsync(cancellation.Token));
        Assert.AreEqual(0, store.MonthReads);
    }

    [TestMethod]
    public void IanaZone_ResolvesThroughPlatformData_AndDefaultIntervalIs60()
    {
        var settings = CampaignAutomationSettings.Read(Config());
        Assert.AreEqual(60, settings.CheckIntervalMinutes);
        var utc = new DateTimeOffset(2026, 11, 1, 5, 59, 0, TimeSpan.Zero);
        Assert.AreEqual(new DateTime(2026, 10, 31, 23, 59, 0), TimeZoneInfo.ConvertTime(utc, settings.TimeZone!).DateTime);
    }
}
