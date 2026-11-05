using EmpleadoDelMes.Api.Automation;
using EmpleadoDelMes.Application.CampaignAutomation;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Campaigns;

[TestClass]
public sealed class PersistentAutomationTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly Clock Clock = new();
        internal readonly CampaignTestRepository Campaigns = new();
        internal readonly AutomationTestStore Store;
        internal readonly CampaignAutomationStatus Status = new();
        internal readonly AutomationTechnicalSettings Technical = new(true,
            TimeZoneInfo.FindSystemTimeZoneById("America/Monterrey"), new DateOnly(2026, 10, 1));
        internal readonly ServiceProvider Provider;
        internal CampaignAutomationService Service => Provider.GetRequiredService<CampaignAutomationService>();
        internal Fixture(bool enabled = false, bool technicalEnabled = true)
        {
            Technical = Technical with { Enabled = technicalEnabled };
            Campaigns.Clock = Clock;
            Store = new(Campaigns); Store.Initialize(enabled);
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ICampaignRepository>(Campaigns);
            services.AddSingleton<ICampaignAutomationStore>(Store);
            services.AddSingleton(Technical); services.AddSingleton(Status);
            services.AddTransient<CampaignService>(); services.AddTransient<CampaignAutomationService>();
            Provider = services.BuildServiceProvider();
        }
        internal CampaignAutomationCycle Cycle() => new(new(Technical.Enabled, Technical.TimeZone, 60,
            Technical.FirstEvaluationMonth!.Value), Provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<CampaignAutomationWorker>.Instance);
        internal Task<AutomationState> Save(bool enabled, AutomationCalendarInput? calendar = null, string? version = null) =>
            Service.UpdateAsync(new(enabled, calendar ?? AutomationTestStore.Calendar,
                version ?? Convert.ToBase64String(Store.Configuration.RowVersion)), Guid.NewGuid(), CancellationToken.None);
        public void Dispose() => Provider.Dispose();
    }

    [TestMethod]
    public async Task InitialConfiguration_IsDisabled_AndDoesNotCreate()
    {
        using var f = new Fixture();
        var state = await f.Service.GetAsync(default);
        Assert.IsFalse(state.Configuration.IsEnabled); Assert.IsFalse(state.EffectiveEnabled);
        Assert.IsNull(state.Configuration.Calendar);
        await f.Cycle().CheckAsync(default);
        Assert.AreEqual(0, f.Campaigns.CreateCalls);
        Assert.IsNotNull(f.Status.Read().LastCheckAtUtc); Assert.IsNull(f.Status.Read().LastGenerationAtUtc);
    }

    [TestMethod]
    public async Task EnablePauseAndHotChanges_AreObservedWithinSameMonth()
    {
        using var f = new Fixture(); var cycle = f.Cycle();
        await cycle.CheckAsync(default);
        await f.Save(true);
        await cycle.CheckAsync(default);
        var before = f.Campaigns.ToDto(f.Campaigns.Campaigns[new(2026, 10, 1)]);
        var lastGenerated = f.Status.Read().LastGenerationAtUtc;
        await f.Save(false); await cycle.CheckAsync(default);
        Assert.AreEqual(2, f.Campaigns.CreateCalls);
        var changed = AutomationTestStore.Calendar with { OpinionClosingDayOffset = 20 };
        await f.Save(true, changed); await cycle.CheckAsync(default);
        Assert.AreEqual(before, f.Campaigns.ToDto(f.Campaigns.Campaigns[new(2026, 10, 1)]));
        Assert.AreEqual(lastGenerated, f.Status.Read().LastGenerationAtUtc);
        f.Clock.Now = f.Clock.Now.AddMonths(1);
        await cycle.CheckAsync(default);
        var next = f.Campaigns.Campaigns[new(2026, 12, 1)];
        Assert.AreEqual(new DateTimeOffset(2027, 1, 22, 0, 0, 0, TimeSpan.Zero), next.OpinionPeriod.ScheduledClosingAtUtc);
        Assert.AreEqual(3, f.Store.Audit.Count);
        StringAssert.Contains(f.Store.Audit[0].PreviousValues, "\"IsEnabled\":false");
        StringAssert.Contains(f.Store.Audit[0].NewValues, "\"IsEnabled\":true");
        Assert.AreEqual(new DateOnly(2026, 12, 1), f.Status.Read().LastGeneratedMonth);
    }

    [TestMethod]
    public async Task TechnicalDisabled_PreventsGenerationEvenWithAdministrativeActivation()
    {
        using var f = new Fixture(enabled: true, technicalEnabled: false);
        await f.Cycle().CheckAsync(default);
        var result = await f.Service.GenerateAsync(new(2026, 10, 1), default);
        Assert.IsFalse(result.Created); Assert.AreEqual(0, f.Campaigns.CreateCalls);
        Assert.IsFalse((await f.Service.GetAsync(default)).EffectiveEnabled);
    }

    [TestMethod]
    public async Task MissingOrInvalidCalendar_CannotBeSavedOrActivated()
    {
        using var f = new Fixture();
        foreach (var calendar in new AutomationCalendarInput?[] { null,
            AutomationTestStore.Calendar with { OpinionClosingLocalTime = "24:00" },
            AutomationTestStore.Calendar with { NominationClosingDayOffset = 31 },
            AutomationTestStore.Calendar with { OpinionClosingDayOffset = 0 },
            AutomationTestStore.Calendar with { NominationOpeningDayOffset = -32 },
            AutomationTestStore.Calendar with { OpinionClosingDayOffset = 366 },
            AutomationTestStore.Calendar with { NominationOpeningLocalTime = "08:00:00" } })
        {
            var error = await Assert.ThrowsAsync<AutomationException>(() => f.Service.UpdateAsync(
                new(true, calendar, Convert.ToBase64String(f.Store.Configuration.RowVersion)), Guid.NewGuid(), default));
            Assert.AreEqual(CampaignError.Validation, error.Error);
        }
        Assert.IsFalse(f.Store.Configuration.IsEnabled); Assert.AreEqual(0, f.Store.Audit.Count);
    }

    [TestMethod]
    public async Task StaleOrMalformedVersion_RejectsWrite()
    {
        using var f = new Fixture();
        var old = Convert.ToBase64String(f.Store.Configuration.RowVersion);
        await f.Save(true);
        var stale = await Assert.ThrowsAsync<AutomationException>(() => f.Save(false, version: old));
        Assert.AreEqual(CampaignError.Concurrency, stale.Error);
        var invalid = await Assert.ThrowsAsync<AutomationException>(() => f.Save(false, version: "invalid"));
        Assert.AreEqual(CampaignError.Validation, invalid.Error);
        Assert.IsTrue(f.Store.Configuration.IsEnabled); Assert.AreEqual(1, f.Store.Audit.Count);
    }

    [TestMethod]
    public async Task ConcurrentEditors_OnlyOneVersionWins()
    {
        using var f = new Fixture();
        var version = Convert.ToBase64String(f.Store.Configuration.RowVersion);
        async Task<bool> Write(bool enabled)
        {
            try { await f.Save(enabled, version: version); return true; }
            catch (AutomationException ex) when (ex.Error == CampaignError.Concurrency) { return false; }
        }
        var results = await Task.WhenAll(Write(true), Write(false));
        Assert.AreEqual(1, results.Count(x => x)); Assert.AreEqual(1, f.Store.Audit.Count);
    }

    [TestMethod]
    public async Task Preview_IsReadOnly_ReportsBacklogExistingAndConflicts()
    {
        using var f = new Fixture();
        var existing = new Campaign(Guid.NewGuid(), new(2026, 10, 1), f.Clock.Now);
        existing.OpenNominations(); f.Campaigns.Campaigns.Add(existing.EvaluationMonth, existing);
        var preview = await f.Service.PreviewAsync(new(AutomationTestStore.Calendar, 3), default);
        Assert.IsTrue(preview.Months.Single(m => m.EvaluationMonth == existing.EvaluationMonth).AlreadyExists);
        Assert.IsTrue(preview.Months.Any(m => m.Conflicts.Count > 0));
        Assert.AreEqual(1, preview.PendingMonths.Count); Assert.AreEqual(0, f.Store.Audit.Count);
        Assert.AreEqual(0, f.Campaigns.CreateCalls);
        var error = await Assert.ThrowsAsync<AutomationException>(() => f.Save(true));
        Assert.AreEqual(CampaignError.Concurrency, error.Error);
        // Pausing remains possible even with exceptional manually opened campaigns.
        await f.Save(false);
        existing.Archive();
        preview = await f.Service.PreviewAsync(new(AutomationTestStore.Calendar), default);
        Assert.IsTrue(preview.Months.First().IsArchived);
        Assert.IsFalse(preview.Months.Any(m => m.Conflicts.Count > 0));
    }

    [TestMethod]
    public async Task StorageFailure_IsControlled_AndNextTickRetries()
    {
        using var f = new Fixture(enabled: true); var cycle = f.Cycle();
        f.Store.FailRead = true; await cycle.CheckAsync(default);
        Assert.IsNotNull(f.Status.Read().Error); Assert.AreEqual(0, f.Campaigns.CreateCalls);
        f.Store.FailRead = false; await cycle.CheckAsync(default);
        Assert.IsNull(f.Status.Read().Error); Assert.AreEqual(2, f.Campaigns.CreateCalls);
    }

    [TestMethod]
    public async Task ConcurrentCycles_CountOnlyActualGeneration()
    {
        using var f = new Fixture(enabled: true);
        await Task.WhenAll(f.Cycle().CheckAsync(default), f.Cycle().CheckAsync(default));
        Assert.AreEqual(2, f.Campaigns.CreateCalls);
        Assert.AreEqual(0, f.Campaigns.DuplicateWrites);
        var last = f.Status.Read().LastGenerationAtUtc;
        f.Clock.Now = f.Clock.Now.AddHours(1); await f.Cycle().CheckAsync(default);
        Assert.AreEqual(last, f.Status.Read().LastGenerationAtUtc);
    }

    [TestMethod]
    public void SqlModel_HasSingletonRowVersionDisabledSeedAndAudit()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer().Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(CampaignAutomationConfiguration))!;
        var version = entity.FindProperty(nameof(CampaignAutomationConfiguration.RowVersion))!;
        Assert.IsTrue(version.IsConcurrencyToken);
        Assert.AreEqual(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.IsFalse((bool)entity.GetSeedData().Single()["IsEnabled"]!);
        Assert.AreEqual(1, entity.GetSeedData().Single()["Id"]);
        Assert.IsNotNull(model.FindEntityType(typeof(CampaignAutomationAuditEntry)));
        StringAssert.Contains(db.Database.GenerateCreateScript(), "CK_Automation_Singleton");
        Assert.IsFalse(db.Database.HasPendingModelChanges());
    }

    // Updated expectations for the new business calendar. Added but not executed in this task.
    [TestMethod]
    [DataRow(2026, 10, 24)]
    [DataRow(2026, 11, 23)]
    [DataRow(2026, 2, 21)]
    [DataRow(2028, 2, 22)]
    [DataRow(2026, 12, 24)]
    public void AnticipatedCalendar_UsesMonthLengthAndPlantTime(int year, int month, int openingDay)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Monterrey");
        var evaluation = new DateOnly(year, month, 1);
        var schedule = AutomationCalendarValidation.Schedule(AutomationCalendarValidation.Parse(
            new(-8, "00:00", 7, "00:00", -8, "00:00", 7, "00:00")), zone);
        var dates = schedule.Calculate(evaluation);
        Assert.AreEqual(new DateTime(year, month, openingDay), TimeZoneInfo.ConvertTime(dates.NominationOpening, zone).DateTime);
        Assert.AreEqual(evaluation.AddMonths(1).AddDays(7).ToDateTime(TimeOnly.MinValue),
            TimeZoneInfo.ConvertTime(dates.NominationClosing, zone).DateTime);
        Assert.AreEqual(dates.NominationOpening, dates.OpinionOpening);
        Assert.AreEqual(dates.NominationClosing, dates.OpinionClosing);
    }

    [TestMethod]
    public async Task CurrentMonth_IsPending_ButFutureMonthCannotBeGenerated()
    {
        using var f = new Fixture(enabled: true);
        var preview = await f.Service.PreviewAsync(new(new(-8, "00:00", 7, "00:00", -8, "00:00", 7, "00:00"), 3), default);
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1) }, preview.PendingMonths.ToArray());
        Assert.IsFalse(preview.Months.Single(m => m.EvaluationMonth == new DateOnly(2026, 12, 1)).IsPending);
        var error = await Assert.ThrowsAsync<AutomationException>(() => f.Service.GenerateAsync(new(2026, 12, 1), default));
        Assert.AreEqual(CampaignError.Validation, error.Error);
        Assert.AreEqual(0, f.Campaigns.CreateCalls);
    }

    [TestMethod]
    public async Task ManualOpeningConflict_IsVisible_AndNotClosedByWorker()
    {
        using var f = new Fixture(enabled: true);
        var blocker = new Campaign(Guid.NewGuid(), new(2026, 2, 1), f.Clock.Now);
        blocker.OpenNominations();
        f.Campaigns.Campaigns.Add(blocker.EvaluationMonth, blocker);
        await f.Cycle().CheckAsync(default);
        StringAssert.Contains(f.Status.Read().Error!, "2026-02");
        Assert.AreEqual(EmpleadoDelMes.Domain.Enums.CampaignPeriodMode.ManuallyOpen, blocker.NominationPeriod.Mode);
        Assert.AreEqual(0, f.Campaigns.CreateCalls);
    }

    [TestMethod]
    public void SchedulePoint_RejectsOffsetsOutsideRangeAndDateOverflow()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Monterrey");
        Assert.Throws<ArgumentException>(() => new CampaignSchedulePoint(-32, TimeOnly.MinValue).ToUtc(new(2026, 10, 1), zone));
        Assert.Throws<ArgumentException>(() => new CampaignSchedulePoint(366, TimeOnly.MinValue).ToUtc(new(2026, 10, 1), zone));
        Assert.Throws<ArgumentException>(() => new CampaignSchedulePoint(-8, TimeOnly.MinValue).ToUtc(new(9999, 12, 1), zone));
    }}
