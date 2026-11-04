using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Campaigns;

// Models a unique month and the duplicate-write outcome, without claiming SQL integration coverage.
internal sealed class CampaignTestRepository : ICampaignRepository
{
    internal readonly Dictionary<DateOnly, Campaign> Campaigns = [];
    internal bool CoordinateTwoReads;
    internal int CreateCalls;
    internal int DuplicateWrites;
    internal CampaignError? Failure;
    internal DateOnly? FailOnMonth;
    internal int MonthReads;
    internal bool FailSave;
    internal TimeProvider Clock = TimeProvider.System;
    private readonly Dictionary<Guid, long> versions = [];
    private readonly SemaphoreSlim updateLock = new(1);
    internal SemaphoreSlim Coordination => updateLock;
    internal CampaignDto ToDto(Campaign c) => CampaignDto.FromDomain(c,
        Convert.ToBase64String(BitConverter.GetBytes(versions.GetValueOrDefault(c.Id, 1))), Clock.GetUtcNow());
    private int reads;
    private readonly TaskCompletionSource bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<CampaignDto?> GetByIdAsync(Guid id, CancellationToken token)
    {
        var campaign = Campaigns.Values.SingleOrDefault(c => c.Id == id);
        return Task.FromResult(campaign is null ? null : ToDto(campaign));
    }

    public async Task<CampaignDto?> GetByMonthAsync(DateOnly month, CancellationToken token)
    {
        Interlocked.Increment(ref MonthReads);
        CampaignDto? result;
        lock (Campaigns) result = Campaigns.TryGetValue(month, out var found) ? ToDto(found) : null;
        if (CoordinateTwoReads)
        {
            var call = Interlocked.Increment(ref reads);
            if (call == 2) bothRead.SetResult();
            if (call <= 2) await bothRead.Task.WaitAsync(token);
        }
        return result;
    }

    public Task<CampaignDto> CreateAsync(Campaign campaign, CancellationToken token)
    {
        lock (Campaigns)
        {
            CreateCalls++;
            if (Failure is { } error && (FailOnMonth is null || FailOnMonth == campaign.EvaluationMonth))
                throw new CampaignPersistenceException(error, new Exception("Internal database detail"));
            if (!Campaigns.ContainsKey(campaign.EvaluationMonth) && Campaigns.Values.Any(other => CampaignNominationPolicy.Conflicts(campaign, other, Clock.GetUtcNow())))
                throw new CampaignPersistenceException(CampaignError.Concurrency);
            if (!Campaigns.TryAdd(campaign.EvaluationMonth, campaign))
            {
                DuplicateWrites++;
                throw new CampaignPersistenceException(CampaignError.Duplicate);
            }
            return Task.FromResult(ToDto(campaign));
        }
    }

    public Task<CampaignPage> ListAsync(int pageNumber, int pageSize, CancellationToken token) => Task.FromResult(
        new CampaignPage(Campaigns.Values.OrderByDescending(c => c.EvaluationMonth).ThenByDescending(c => c.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(ToDto).ToArray(), Campaigns.Count, pageNumber, pageSize));

    public async Task<CampaignResult<CampaignDto>> UpdateAsync(Guid id, byte[] expectedVersion,
        Func<Campaign, IReadOnlyList<Campaign>, CampaignResult<bool>> change, CancellationToken token)
    {
        await updateLock.WaitAsync(token);
        try
        {
            await Task.Yield();
            var current = Campaigns.Values.SingleOrDefault(c => c.Id == id);
            if (current is null) return new(null, CampaignError.NotFound, "No encontramos la convocatoria.");
            if (ToDto(current).Version != Convert.ToBase64String(expectedVersion)) return new(null, CampaignError.Concurrency, "La convocatoria cambió.");
            var copy = Clone(current);
            var result = change(copy, Campaigns.Values.Where(c => c.Id != id).ToArray());
            if (!result.IsSuccess) return new(null, result.Error, result.Message);
            if (FailSave) return new(null, CampaignError.Persistence, "No pudimos guardar la convocatoria.");
            if (ToDto(current) != ToDto(copy)) versions[id] = versions.GetValueOrDefault(id, 1) + 1;
            Campaigns[current.EvaluationMonth] = copy;
            return new(ToDto(copy));
        }
        finally { updateLock.Release(); }
    }

    private static Campaign Clone(Campaign c)
    {
        var copy = new Campaign(c.Id, c.EvaluationMonth, c.CreatedAtUtc);
        if (c.NominationPeriod.ScheduledOpeningAtUtc is { } n) copy.ScheduleNominations(n, c.NominationPeriod.ScheduledClosingAtUtc!.Value);
        if (c.OpinionPeriod.ScheduledOpeningAtUtc is { } o) copy.ScheduleOpinions(o, c.OpinionPeriod.ScheduledClosingAtUtc!.Value);
        if (c.NominationPeriod.Mode == CampaignPeriodMode.ManuallyOpen) copy.OpenNominations();
        if (c.NominationPeriod.Mode == CampaignPeriodMode.Automatic) copy.EnableAutomaticNominations();
        if (c.OpinionPeriod.Mode == CampaignPeriodMode.ManuallyOpen) copy.OpenOpinions();
        if (c.OpinionPeriod.Mode == CampaignPeriodMode.Automatic) copy.EnableAutomaticOpinions();
        if (c.IsArchived) copy.Archive();
        return copy;
    }
}

[TestClass]
public sealed class CampaignServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private static CampaignService Service(CampaignTestRepository repository) => new(repository, new Clock());

    [TestMethod]
    public async Task ManualCreation_UsesPlantCalendarAndClosedDomainDefaults()
    {
        var repository = new CampaignTestRepository(); var service = Service(repository);
        // UTC is November 1, but Monterrey is still October 31.
        Assert.AreEqual(CampaignError.Validation, (await service.CreateManualAsync(new(2026, 10, 1))).Error);
        Assert.AreEqual(CampaignError.Validation, (await service.CreateManualAsync(new(2026, 11, 1))).Error);
        Assert.AreEqual(CampaignError.Validation, (await service.CreateManualAsync(new(2027, 1, 1))).Error);
        Assert.AreEqual(CampaignError.Validation, (await service.CreateManualAsync(new(2026, 9, 2))).Error);
        Assert.AreEqual(CampaignError.Validation, (await service.CreateManualAsync(null)).Error);
        var created = (await service.CreateManualAsync(new(2026, 9, 1))).Value!;
        Assert.AreEqual(Now, created.CreatedAtUtc);
        Assert.AreEqual(new DateOnly(2026, 9, 1), created.EvaluationMonth);
        Assert.IsFalse(created.IsArchived);
        foreach (var period in new[] { created.NominationPeriod, created.OpinionPeriod })
        {
            Assert.AreEqual(CampaignPeriodMode.ManuallyClosed, period.Mode);
            Assert.IsNull(period.ScheduledOpeningAtUtc);
            Assert.IsNull(period.ScheduledClosingAtUtc);
        }
        repository.Campaigns[created.EvaluationMonth].OpenOpinions();
        var before = repository.ToDto(repository.Campaigns[created.EvaluationMonth]);
        var duplicate = await service.CreateManualAsync(created.EvaluationMonth);
        Assert.AreEqual(CampaignError.Duplicate, duplicate.Error);
        Assert.AreEqual(before, repository.ToDto(repository.Campaigns[created.EvaluationMonth]));
        Assert.AreEqual(1, repository.CreateCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ManualAndAutomaticCreation_ShareUniqueMonthProtection(bool manualStartsFirst)
    {
        var repository = new CampaignTestRepository { CoordinateTwoReads = true };
        var service = Service(repository);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<CampaignResult<CampaignDto>> manual, automatic;
        if (manualStartsFirst)
        {
            manual = service.CreateManualAsync(new(2026, 9, 1), timeout.Token);
            automatic = service.EnsurePreviousMonthAsync(new(2026, 10, 3), timeout.Token);
        }
        else
        {
            automatic = service.EnsurePreviousMonthAsync(new(2026, 10, 3), timeout.Token);
            manual = service.CreateManualAsync(new(2026, 9, 1), timeout.Token);
        }
        await Task.WhenAll(manual, automatic);
        Assert.IsTrue(automatic.Result.IsSuccess);
        Assert.IsTrue(manual.Result.IsSuccess || manual.Result.Error == CampaignError.Duplicate);
        Assert.AreEqual(1, repository.Campaigns.Count);
        Assert.AreEqual(1, repository.DuplicateWrites);
        Assert.AreEqual(repository.Campaigns.Values.Single().Id, automatic.Result.Value!.Id);
    }

    [TestMethod]
    public async Task ManualCreation_PersistenceFailureIsControlled()
    {
        var repository = new CampaignTestRepository { Failure = CampaignError.Persistence };
        var result = await Service(repository).CreateManualAsync(new(2026, 9, 1));
        Assert.AreEqual(CampaignError.Persistence, result.Error);
        Assert.IsFalse(result.Message!.Contains("Internal"));
        Assert.AreEqual(0, repository.Campaigns.Count);
    }

    [TestMethod]
    [DataRow(2026, 11, 3, 2026, 10)]
    [DataRow(2027, 1, 5, 2026, 12)]
    [DataRow(2026, 10, 31, 2026, 9)]
    public async Task Ensure_UsesExplicitLocalDateAndUtcClock(int year, int month, int day, int expectedYear, int expectedMonth)
    {
        var repository = new CampaignTestRepository();
        var result = await Service(repository).EnsurePreviousMonthAsync(new(year, month, day));
        Assert.IsTrue(result.IsSuccess);
        var campaign = result.Value!;
        Assert.AreEqual(new DateOnly(expectedYear, expectedMonth, 1), campaign.EvaluationMonth);
        Assert.AreEqual(Now, campaign.CreatedAtUtc);
        Assert.AreNotEqual(Guid.Empty, campaign.Id);
        Assert.IsFalse(campaign.IsArchived);
        Assert.AreEqual(1, repository.CreateCalls);
        foreach (var period in new[] { campaign.NominationPeriod, campaign.OpinionPeriod })
        {
            Assert.AreEqual(CampaignPeriodMode.ManuallyClosed, period.Mode);
            Assert.IsNull(period.ScheduledOpeningAtUtc);
            Assert.IsNull(period.ScheduledClosingAtUtc);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RepeatedEnsure_PreservesIdentityDatesPeriodsAndArchive(bool archived)
    {
        var repository = new CampaignTestRepository(); var service = Service(repository);
        var first = (await service.EnsurePreviousMonthAsync(new(2026, 11, 3))).Value!;
        var campaign = repository.Campaigns[first.EvaluationMonth];
        campaign.ScheduleNominations(Now, Now.AddDays(1)); campaign.EnableAutomaticNominations();
        campaign.ScheduleOpinions(Now.AddDays(2), Now.AddDays(3)); campaign.OpenOpinions();
        if (archived) campaign.Archive();
        var before = repository.ToDto(campaign);
        var again = (await service.EnsurePreviousMonthAsync(new(2026, 11, 30))).Value!;
        Assert.AreEqual(before, again); Assert.AreEqual(1, repository.CreateCalls); Assert.AreEqual(1, repository.Campaigns.Count);
    }

    [TestMethod]
    public async Task ConcurrentCreation_RecoversWinnerAfterDuplicate()
    {
        var repository = new CampaignTestRepository { CoordinateTwoReads = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var results = await Task.WhenAll(
            Service(repository).EnsurePreviousMonthAsync(new(2026, 11, 3), timeout.Token),
            Service(repository).EnsurePreviousMonthAsync(new(2026, 11, 4), timeout.Token));
        Assert.IsTrue(results.All(r => r.IsSuccess));
        Assert.AreEqual(results[0].Value, results[1].Value);
        Assert.AreEqual(1, repository.Campaigns.Count); Assert.AreEqual(1, repository.DuplicateWrites);
    }

    [TestMethod]
    public async Task Queries_FindIdAndNormalizeMonth_WithoutCreating()
    {
        var repository = new CampaignTestRepository(); var service = Service(repository);
        var created = (await service.EnsurePreviousMonthAsync(new(2026, 11, 3))).Value!;
        Assert.AreEqual(created, (await service.GetByIdAsync(created.Id)).Value);
        Assert.AreEqual(created, (await service.GetByMonthAsync(new(2026, 10, 31))).Value);
        Assert.AreEqual(CampaignError.NotFound, (await service.GetByIdAsync(Guid.NewGuid())).Error);
        Assert.AreEqual(CampaignError.NotFound, (await service.GetByMonthAsync(new(2025, 1, 1))).Error);
        Assert.AreEqual(1, repository.CreateCalls);
    }

    [TestMethod]
    public async Task List_ReturnsDescendingPageAndTotal()
    {
        var repository = new CampaignTestRepository(); var service = Service(repository);
        foreach (var month in new[] { 8, 10, 9, 11 }) await service.EnsurePreviousMonthAsync(new(2026, month, 3));
        var page = (await service.ListAsync(2, 2)).Value!;
        Assert.AreEqual(4, page.Total); Assert.AreEqual(2, page.PageNumber); Assert.AreEqual(2, page.PageSize);
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1) }, page.Items.Select(c => c.EvaluationMonth).ToArray());
        Assert.AreEqual(0, (await service.ListAsync(3, 2)).Value!.Items.Count);
    }

    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(1, 0)]
    [DataRow(1, 101)]
    [DataRow(int.MaxValue, 100)]
    public async Task InvalidPagination_IsControlled(int page, int size) =>
        Assert.AreEqual(CampaignError.Validation, (await Service(new()).ListAsync(page, size)).Error);

    [TestMethod]
    public async Task InvalidIdAndReferenceDate_AreControlled()
    {
        var repository = new CampaignTestRepository(); var service = Service(repository);
        Assert.AreEqual(CampaignError.Validation, (await service.GetByIdAsync(Guid.Empty)).Error);
        Assert.AreEqual(CampaignError.Validation, (await service.EnsurePreviousMonthAsync(DateOnly.MinValue)).Error);
        Assert.AreEqual(0, repository.CreateCalls);
    }

    [TestMethod]
    public async Task PersistenceFailure_DoesNotExposeInternalMessage()
    {
        var result = await Service(new() { Failure = CampaignError.Persistence }).EnsurePreviousMonthAsync(new(2026, 11, 3));
        Assert.AreEqual(CampaignError.Persistence, result.Error);
        Assert.IsFalse(result.Message!.Contains("Internal"));
    }
}
