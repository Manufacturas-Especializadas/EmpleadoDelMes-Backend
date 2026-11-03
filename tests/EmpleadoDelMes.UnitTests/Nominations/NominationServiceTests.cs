using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using EmpleadoDelMes.Infrastructure.Identity;
using EmpleadoDelMes.UnitTests.Accounts;
using EmpleadoDelMes.UnitTests.Campaigns;
using Microsoft.AspNetCore.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Nominations;

internal sealed class NominationTestRepository(AccountTestStore accounts, CampaignTestRepository campaigns) : INominationRepository
{
    internal readonly List<Nomination> Nominations = [];
    internal bool FailSave;
    public Task<NominationActor?> GetActorAsync(Guid userId, CancellationToken token)
    {
        if (!accounts.Users.TryGetValue(userId, out var u)) return Task.FromResult<NominationActor?>(null);
        var employee = u.EmployeeId is { } id && accounts.Employees.TryGetValue(id, out var e) ? new Employee(e.Id, e.PayrollNumber, e.FullName) : null;
        return Task.FromResult<NominationActor?>(new(u.Id, employee, u.IsActive, u.MustChangePassword,
            accounts.Memberships.GetValueOrDefault(u.Id, []).Contains(AccountRoles.Sponsor.ToUpperInvariant()),
            u.LockoutEnabled && u.LockoutEnd > campaigns.Clock.GetUtcNow()));
    }
    public Task<IReadOnlyList<Campaign>> GetPotentialCampaignsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<Campaign>>(campaigns.Campaigns.Values.ToArray());
    public async Task<NominationResult<NominationConfirmation>> CreateAsync(Guid userId, Guid candidateId,
        Func<NominationCreationContext, NominationResult<Nomination>> create, CancellationToken token)
    {
        await campaigns.Coordination.WaitAsync(token);
        try
        {
            var candidate = accounts.Employees.TryGetValue(candidateId, out var e) ? new Employee(e.Id, e.PayrollNumber, e.FullName) : null;
            var result = create(new(await GetActorAsync(userId, token), candidate, await GetPotentialCampaignsAsync(token)));
            if (!result.IsSuccess) return new(null, result.Error, result.Message);
            var n = result.Value!;
            if (Nominations.Any(old => old.CampaignId == n.CampaignId && old.SponsorEmployeeId == n.SponsorEmployeeId && old.CandidateEmployeeId == n.CandidateEmployeeId))
                throw new NominationPersistenceException(NominationError.Duplicate);
            if (FailSave) throw new NominationPersistenceException(NominationError.Persistence, new Exception("SQL internal detail"));
            Nominations.Add(n); return new(NominationConfirmation.FromDomain(n));
        }
        finally { campaigns.Coordination.Release(); }
    }
}

[TestClass]
public sealed class NominationServiceTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture
    {
        internal readonly Clock Clock = new();
        internal readonly AccountTestStore Accounts = new();
        internal readonly CampaignTestRepository Campaigns;
        internal readonly NominationTestRepository Store;
        internal readonly NominationService Service;
        internal readonly Guid UserId = Guid.NewGuid();
        internal readonly EmployeeDto Sponsor = new(Guid.NewGuid(), "007", "Ana Pérez", "");
        internal readonly EmployeeDto Candidate = new(Guid.NewGuid(), "123", "Luis López", "");
        internal Campaign Campaign => Campaigns.Campaigns.Values.First();
        internal Fixture()
        {
            Campaigns = new() { Clock = Clock };
            var campaign = new Campaign(Guid.NewGuid(), new(2026, 10, 1), Clock.Now); campaign.OpenNominations();
            Campaigns.Campaigns.Add(campaign.EvaluationMonth, campaign);
            Accounts.Employees.Add(Sponsor.Id, Sponsor); Accounts.Employees.Add(Candidate.Id, Candidate);
            Accounts.Users.Add(UserId, new ApplicationUser { Id = UserId, EmployeeId = Sponsor.Id, IsActive = true });
            Accounts.Memberships.Add(UserId, [AccountRoles.Sponsor.ToUpperInvariant()]);
            Store = new(Accounts, Campaigns); Service = new(Store, new EmployeeService(Accounts), Clock);
        }
        internal Task<NominationResult<NominationConfirmation>> Create(Guid? candidate = null, int qualities = 5, string? text = "  Colabora y cumple sus compromisos.  ", Guid? user = null) =>
            Service.CreateAsync(user ?? UserId, candidate ?? Candidate.Id, (NominationQualities)qualities, text);
    }

    [TestMethod]
    public async Task ValidNomination_UsesAccountIdentityAndCatalogSnapshots()
    {
        var f = new Fixture(); var result = await f.Create(); Assert.IsTrue(result.IsSuccess);
        var n = result.Value!;
        Assert.AreEqual(f.UserId, n.CreatedByUserId); Assert.AreEqual(f.Sponsor.Id, n.Sponsor.Id);
        Assert.AreEqual("007", n.Sponsor.PayrollNumber); Assert.AreEqual("Ana Pérez", n.Sponsor.FullName);
        Assert.AreEqual("123", n.Candidate.PayrollNumber); Assert.AreEqual("Luis López", n.Candidate.FullName);
        Assert.AreEqual(f.Clock.Now, n.RegisteredAtUtc); Assert.IsFalse(n.IsCancelled);
        Assert.AreEqual("Colabora y cumple sus compromisos.", n.Justification);
        Assert.AreEqual((NominationQualities)5, n.Qualities);
        f.Accounts.Employees[f.Sponsor.Id] = f.Sponsor with { FullName = "Nombre corregido" };
        Assert.AreEqual("Ana Pérez", f.Store.Nominations.Single().SponsorFullName);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(16)] [DataRow(17)] [DataRow(-1)]
    public async Task InvalidQualities_AreRejected(int qualities)
    {
        var f = new Fixture(); Assert.AreEqual(NominationError.Validation, (await f.Create(qualities: qualities)).Error);
        Assert.AreEqual(0, f.Store.Nominations.Count);
    }

    [TestMethod]
    [DataRow(null)] [DataRow("")] [DataRow("   ")]
    public async Task EmptyJustification_IsRejected(string? text) =>
        Assert.AreEqual(NominationError.Validation, (await new Fixture().Create(text: text)).Error);

    [TestMethod]
    public async Task JustificationLimit_AndEmployeeErrors()
    {
        var f = new Fixture();
        Assert.AreEqual(NominationError.Validation, (await f.Create(text: new string('a', 501))).Error);
        Assert.AreEqual(NominationError.Validation, (await f.Create(candidate: f.Sponsor.Id)).Error);
        Assert.AreEqual(NominationError.NotFound, (await f.Create(candidate: Guid.NewGuid())).Error);
        Assert.IsTrue((await f.Create(text: new string('a', 500))).IsSuccess);
    }

    [TestMethod]
    public async Task HistoricalUniqueness_AllowsDifferentCandidatesAndSponsorsButNotCancelledDuplicates()
    {
        var f = new Fixture(); Assert.IsTrue((await f.Create()).IsSuccess);
        f.Store.Nominations[0].Cancel(); Assert.AreEqual(NominationError.Duplicate, (await f.Create()).Error);
        var other = new EmployeeDto(Guid.NewGuid(), "124", "Otro empleado", ""); f.Accounts.Employees.Add(other.Id, other);
        Assert.IsTrue((await f.Create(candidate: other.Id)).IsSuccess);
        var user = Guid.NewGuid(); f.Accounts.Users.Add(user, new ApplicationUser { Id = user, EmployeeId = other.Id });
        f.Accounts.Memberships.Add(user, [AccountRoles.Sponsor.ToUpperInvariant()]);
        Assert.IsTrue((await f.Create(user: user)).IsSuccess); Assert.AreEqual(3, f.Store.Nominations.Count);
    }

    [TestMethod]
    public async Task PeriodModesAndArchive_AreRespectedAtDecisionTime()
    {
        var f = new Fixture(); f.Campaign.CloseNominations();
        Assert.AreEqual(NominationError.NoActiveCampaign, (await f.Create()).Error);
        f.Campaign.ScheduleNominations(f.Clock.Now.AddHours(1), f.Clock.Now.AddHours(2)); f.Campaign.EnableAutomaticNominations();
        Assert.AreEqual(NominationError.NoActiveCampaign, (await f.Create()).Error);
        f.Clock.Now = f.Clock.Now.AddHours(1); Assert.IsTrue((await f.Service.GetActiveCampaignAsync(f.UserId)).IsSuccess);
        f.Clock.Now = f.Clock.Now.AddHours(1); Assert.AreEqual(NominationError.NoActiveCampaign, (await f.Create()).Error);
        f.Campaign.OpenNominations(); Assert.IsTrue((await f.Service.GetActiveCampaignAsync(f.UserId)).IsSuccess);
        f.Campaign.Archive(); Assert.AreEqual(NominationError.NoActiveCampaign, (await f.Create()).Error);
        Assert.AreEqual(0, f.Store.Nominations.Count);
    }

    [TestMethod]
    public async Task ActiveCampaign_IsNotNecessarilyLatest_AndMultipleOpenFailClosed()
    {
        var f = new Fixture(); var newer = new Campaign(Guid.NewGuid(), new(2026, 11, 1), f.Clock.Now);
        f.Campaigns.Campaigns.Add(newer.EvaluationMonth, newer);
        Assert.AreEqual(f.Campaign.Id, (await f.Service.GetActiveCampaignAsync(f.UserId)).Value!.Id);
        newer.OpenNominations(); Assert.AreEqual(NominationError.Concurrency, (await f.Create()).Error);
        Assert.AreEqual(0, f.Store.Nominations.Count);
    }

    [TestMethod]
    public async Task AdministrativeCloseWhileWaiting_PreventsInsertion()
    {
        var f = new Fixture(); Assert.IsTrue((await f.Service.GetActiveCampaignAsync(f.UserId)).IsSuccess);
        await f.Campaigns.Coordination.WaitAsync();
        var pending = f.Create(); Assert.IsFalse(pending.IsCompleted);
        // Simulates an administrator holding the SAME lock and committing the close first.
        f.Campaign.CloseNominations(); f.Campaigns.Coordination.Release();
        Assert.AreEqual(NominationError.NoActiveCampaign, (await pending).Error); Assert.AreEqual(0, f.Store.Nominations.Count);
    }

    [TestMethod]
    public async Task ConcurrentDuplicates_OnlyOneSucceeds()
    {
        var f = new Fixture(); var results = await Task.WhenAll(f.Create(), f.Create());
        Assert.AreEqual(1, results.Count(r => r.IsSuccess)); Assert.AreEqual(1, f.Store.Nominations.Count);
        Assert.AreEqual(NominationError.Duplicate, results.Single(r => !r.IsSuccess).Error);
    }

    [TestMethod]
    public async Task AccountStateAndEmployeeLink_AreCheckedAgain()
    {
        var f = new Fixture(); var user = f.Accounts.Users[f.UserId];
        user.IsActive = false; Assert.AreEqual(NominationError.Unauthorized, (await f.Create()).Error);
        user.IsActive = true; user.MustChangePassword = true; Assert.AreEqual(NominationError.Forbidden, (await f.Create()).Error);
        user.MustChangePassword = false; user.EmployeeId = null; Assert.AreEqual(NominationError.Forbidden, (await f.Create()).Error);
        user.EmployeeId = Guid.NewGuid(); Assert.AreEqual(NominationError.Forbidden, (await f.Create()).Error);
        user.EmployeeId = f.Sponsor.Id; f.Accounts.Memberships.Clear(); Assert.AreEqual(NominationError.Forbidden, (await f.Create()).Error);
        f.Accounts.Users.Clear(); Assert.AreEqual(NominationError.Unauthorized, (await f.Create()).Error);
        Assert.AreEqual(0, f.Store.Nominations.Count);
    }

    [TestMethod]
    public async Task Search_IsBoundedAndContainsOnlyCandidateFields()
    {
        var f = new Fixture();
        for (var i = 0; i < 30; i++)
        {
            var e = new EmployeeDto(Guid.NewGuid(), $"E{i:000}", "Candidato", "private-version"); f.Accounts.Employees.Add(e.Id, e);
        }
        var page = (await f.Service.SearchCandidatesAsync(f.UserId, "Candidato", 2, 10)).Value!;
        Assert.AreEqual(30, page.Total); Assert.AreEqual(10, page.Items.Count); Assert.AreEqual("E010", page.Items[0].PayrollNumber);
        Assert.AreEqual("007", (await f.Service.SearchCandidatesAsync(f.UserId, "007")).Value!.Items.Single(e => e.PayrollNumber == "007").PayrollNumber);
        Assert.AreEqual(NominationError.Validation, (await f.Service.SearchCandidatesAsync(f.UserId, "")).Error);
        Assert.AreEqual(NominationError.Validation, (await f.Service.SearchCandidatesAsync(f.UserId, "Candidato", 1, 101)).Error);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(page).Contains("Version"));
    }

    [TestMethod]
    public async Task PersistenceFailure_DoesNotWriteOrExposeInternalDetails()
    {
        var f = new Fixture(); f.Store.FailSave = true;
        var result = await f.Create(); Assert.AreEqual(NominationError.Persistence, result.Error);
        Assert.IsFalse(result.Message!.Contains("SQL")); Assert.AreEqual(0, f.Store.Nominations.Count);
    }
}
