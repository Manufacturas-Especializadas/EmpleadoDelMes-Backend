using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Campaigns;

[TestClass]
public sealed class CampaignAdministrationTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture
    {
        internal readonly Clock Clock = new();
        internal readonly CampaignTestRepository Store;
        internal readonly CampaignService Service;
        internal Fixture() { Store = new() { Clock = Clock }; Service = new(Store, Clock); }
        internal CampaignDto Add(int month)
        {
            var campaign = new Campaign(Guid.NewGuid(), new(2026, month, 1), Clock.Now);
            Store.Campaigns.Add(campaign.EvaluationMonth, campaign); return Store.ToDto(campaign);
        }
        internal Task<CampaignResult<CampaignDto>> Change(CampaignDto c, CampaignChange action,
            DateTimeOffset? from = null, DateTimeOffset? to = null) => Service.ChangeAsync(c.Id, c.Version, action, from, to);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SchedulingAndModes_RespectUtcBoundariesAndExplicitChanges(bool nominations)
    {
        var f = new Fixture(); var c = f.Add(10);
        var schedule = nominations ? CampaignChange.ScheduleNominations : CampaignChange.ScheduleOpinions;
        var automatic = nominations ? CampaignChange.AutomaticNominations : CampaignChange.AutomaticOpinions;
        var open = nominations ? CampaignChange.OpenNominations : CampaignChange.OpenOpinions;
        var close = nominations ? CampaignChange.CloseNominations : CampaignChange.CloseOpinions;
        CampaignPeriodDto Period(CampaignDto dto) => nominations ? dto.NominationPeriod : dto.OpinionPeriod;
        bool Accepts(CampaignDto dto) => nominations ? dto.CanAcceptNominations : dto.CanAcceptOpinions;
        Assert.AreEqual(CampaignError.Validation, (await f.Change(c, automatic)).Error);
        var local = f.Clock.Now.ToOffset(TimeSpan.FromHours(-6));
        c = (await f.Change(c, schedule, local, local.AddHours(1))).Value!;
        Assert.AreEqual(TimeSpan.Zero, Period(c).ScheduledOpeningAtUtc!.Value.Offset);
        Assert.AreEqual(f.Clock.Now, Period(c).ScheduledOpeningAtUtc);
        Assert.AreEqual(CampaignPeriodMode.ManuallyClosed, Period(c).Mode);
        c = (await f.Change(c, automatic)).Value!; Assert.IsTrue(Accepts(c));
        f.Clock.Now = local.ToUniversalTime().AddTicks(-1);
        Assert.IsFalse(Accepts((await f.Service.GetByIdAsync(c.Id)).Value!));
        f.Clock.Now = local.ToUniversalTime().AddHours(1);
        Assert.IsFalse(Accepts((await f.Service.GetByIdAsync(c.Id)).Value!));
        c = (await f.Change(c, open)).Value!; Assert.IsTrue(Accepts(c));
        c = (await f.Change(c, schedule, local.AddHours(3), local.AddHours(4))).Value!;
        Assert.AreEqual(CampaignPeriodMode.ManuallyOpen, Period(c).Mode); Assert.IsTrue(Accepts(c));
        c = (await f.Change(c, automatic)).Value!; Assert.IsFalse(Accepts(c));
        f.Clock.Now = local.ToUniversalTime().AddHours(3);
        c = (await f.Change(c, close)).Value!; Assert.IsFalse(Accepts(c));
        c = (await f.Change(c, automatic)).Value!; Assert.IsTrue(Accepts(c));
    }

    [TestMethod]
    [DataRow(CampaignChange.ScheduleNominations)]
    [DataRow(CampaignChange.ScheduleOpinions)]
    public async Task InvalidSchedules_AreRejectedWithoutChanges(CampaignChange action)
    {
        var f = new Fixture(); var c = f.Add(10);
        foreach (var end in new[] { f.Clock.Now, f.Clock.Now.AddHours(-1) })
            Assert.AreEqual(CampaignError.Validation, (await f.Change(c, action, f.Clock.Now, end)).Error);
        Assert.AreEqual(c, (await f.Service.GetByIdAsync(c.Id)).Value);
    }

    [TestMethod]
    public async Task ArchiveAndReopen_AreSafeAndRepeatedCallsDoNotCloseAnAlreadyReopenedCampaign()
    {
        var f = new Fixture(); var c = f.Add(10);
        c = (await f.Change(c, CampaignChange.ScheduleNominations, f.Clock.Now, f.Clock.Now.AddHours(1))).Value!;
        c = (await f.Change(c, CampaignChange.OpenNominations)).Value!;
        c = (await f.Change(c, CampaignChange.OpenOpinions)).Value!;
        c = (await f.Change(c, CampaignChange.Archive)).Value!;
        Assert.IsTrue(c.IsArchived); Assert.IsFalse(c.CanAcceptNominations || c.CanAcceptOpinions);
        Assert.AreEqual(c, (await f.Change(c, CampaignChange.Archive)).Value);
        foreach (var action in new[] { CampaignChange.ScheduleNominations, CampaignChange.ScheduleOpinions,
            CampaignChange.OpenNominations, CampaignChange.OpenOpinions, CampaignChange.AutomaticNominations, CampaignChange.AutomaticOpinions })
            Assert.AreEqual(CampaignError.Validation, (await f.Change(c, action, f.Clock.Now, f.Clock.Now.AddHours(1))).Error);
        c = (await f.Change(c, CampaignChange.Reopen)).Value!;
        Assert.IsFalse(c.IsArchived || c.CanAcceptNominations || c.CanAcceptOpinions);
        Assert.AreEqual(f.Clock.Now, c.NominationPeriod.ScheduledOpeningAtUtc);
        c = (await f.Change(c, CampaignChange.OpenOpinions)).Value!;
        Assert.AreEqual(c, (await f.Change(c, CampaignChange.Reopen)).Value);
    }

    [TestMethod]
    public async Task AutomaticWindows_CannotOverlap_ButCanBeAdjacent_AndOpinionWindowsAreIndependent()
    {
        var f = new Fixture(); var a = f.Add(9); var b = f.Add(10);
        a = (await f.Change(a, CampaignChange.ScheduleNominations, f.Clock.Now, f.Clock.Now.AddHours(2))).Value!;
        a = (await f.Change(a, CampaignChange.AutomaticNominations)).Value!;
        b = (await f.Change(b, CampaignChange.ScheduleNominations, f.Clock.Now.AddHours(1), f.Clock.Now.AddHours(3))).Value!;
        Assert.AreEqual(CampaignError.Concurrency, (await f.Change(b, CampaignChange.AutomaticNominations)).Error);
        Assert.AreEqual(b, (await f.Service.GetByIdAsync(b.Id)).Value);
        b = (await f.Change(b, CampaignChange.ScheduleNominations, f.Clock.Now.AddHours(2), f.Clock.Now.AddHours(3))).Value!;
        b = (await f.Change(b, CampaignChange.AutomaticNominations)).Value!;
        Assert.AreEqual(CampaignError.Concurrency,
            (await f.Change(b, CampaignChange.ScheduleNominations, f.Clock.Now, f.Clock.Now.AddHours(3))).Error);
        a = (await f.Change(a, CampaignChange.OpenOpinions)).Value!;
        b = (await f.Change(b, CampaignChange.OpenOpinions)).Value!;
        Assert.IsTrue(a.CanAcceptOpinions && b.CanAcceptOpinions);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ManualAndFutureAutomaticConflict_InEitherDirection(bool manualFirst)
    {
        var f = new Fixture(); var a = f.Add(9); var b = f.Add(10);
        b = (await f.Change(b, CampaignChange.ScheduleNominations, f.Clock.Now.AddDays(1), f.Clock.Now.AddDays(2))).Value!;
        if (manualFirst)
        {
            a = (await f.Change(a, CampaignChange.OpenNominations)).Value!;
            Assert.AreEqual(CampaignError.Concurrency, (await f.Change(b, CampaignChange.AutomaticNominations)).Error);
        }
        else
        {
            b = (await f.Change(b, CampaignChange.AutomaticNominations)).Value!;
            Assert.AreEqual(CampaignError.Concurrency, (await f.Change(a, CampaignChange.OpenNominations)).Error);
            f.Clock.Now = f.Clock.Now.AddDays(2);
            Assert.IsTrue((await f.Change(a, CampaignChange.OpenNominations)).IsSuccess);
        }
    }

    [TestMethod]
    public async Task ConcurrentManualOpenings_OnlyOneSucceeds()
    {
        var f = new Fixture(); var a = f.Add(9); var b = f.Add(10);
        var results = await Task.WhenAll(f.Change(a, CampaignChange.OpenNominations), f.Change(b, CampaignChange.OpenNominations));
        Assert.AreEqual(1, results.Count(r => r.IsSuccess));
        Assert.AreEqual(CampaignError.Concurrency, results.Single(r => !r.IsSuccess).Error);
        Assert.AreEqual(1, f.Store.Campaigns.Values.Count(c => c.CanAcceptNominations(f.Clock.Now)));
    }

    [TestMethod]
    public async Task StaleVersionAndPersistenceFailure_DoNotSavePartialChanges()
    {
        var f = new Fixture(); var original = f.Add(10);
        var current = (await f.Change(original, CampaignChange.OpenOpinions)).Value!;
        Assert.AreNotEqual(original.Version, current.Version);
        Assert.AreEqual(CampaignError.Concurrency, (await f.Change(original, CampaignChange.Archive)).Error);
        f.Store.FailSave = true;
        Assert.AreEqual(CampaignError.Persistence, (await f.Change(current, CampaignChange.Archive)).Error);
        Assert.AreEqual(current, (await f.Service.GetByIdAsync(current.Id)).Value);
    }

    [TestMethod]
    public async Task VersionAndMissingCampaignErrors_AreControlled()
    {
        var f = new Fixture(); var c = f.Add(10);
        foreach (var version in new string?[] { null, "", "invalid", "AQ==" })
            Assert.AreEqual(CampaignError.Validation, (await f.Service.ChangeAsync(c.Id, version, CampaignChange.Archive)).Error);
        Assert.AreEqual(CampaignError.NotFound, (await f.Service.ChangeAsync(Guid.NewGuid(), c.Version, CampaignChange.Archive)).Error);
    }
}
