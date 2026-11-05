using EmpleadoDelMes.Application.CampaignAutomation;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.UnitTests.Nominations;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.UnitTests.Campaigns;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Infrastructure.Identity;
using EmpleadoDelMes.UnitTests.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Authentication;

// This controller is discovered only by the test host, never by the production API.
[ApiController]
[Route("test/authorization")]
public sealed class AuthorizationProbeController : ControllerBase
{
    [HttpGet("admin")]
    [Authorize(Policy = AccountRoles.Administrator)]
    public IActionResult Admin() => Ok();
    [HttpGet("authenticated")]
    [Authorize]
    public IActionResult Authenticated() => Ok();
    [HttpGet("fallback")]
    public IActionResult Fallback() => Ok();
}

[TestClass]
public sealed class AuthHttpTests
{
    private sealed class Host : WebApplicationFactory<Program>
    {
        internal readonly AccountTestStore Store = new();
        internal readonly CampaignTestRepository Campaigns = new();
        internal NominationTestRepository Nominations = null!;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // Keep test diagnostics in-process; never write to the Windows Event Log.
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["AdminBootstrap:Enabled"] = "false", ["CampaignAutomation:Enabled"] = "false" }));
            builder.ConfigureServices(services =>
            {
                Nominations = new NominationTestRepository(Store, Campaigns);
                services.AddSingleton<INominationRepository>(Nominations);
                services.AddSingleton<IUserStore<ApplicationUser>>(Store);
                services.AddSingleton<IRoleStore<IdentityRole<Guid>>>(Store);
                services.AddSingleton<IEmployeeRepository>(Store);
                services.AddSingleton<IAccountTransactionRunner>(Store);
                services.AddSingleton<ICampaignRepository>(Campaigns);
                var automationStore = new AutomationTestStore(Campaigns);
                automationStore.Initialize(false);
                services.AddSingleton<ICampaignAutomationStore>(automationStore);
                services.AddSingleton<ISponsorImportDirectory>(new SponsorTestDirectory(Store));
                services.AddControllers().AddApplicationPart(typeof(AuthorizationProbeController).Assembly);
            });
        }
        internal HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        internal async Task<CreatedAccount> Sponsor(bool temporary = false)
        {
            var employee = new EmployeeDto(Guid.NewGuid(), "007", "Ana Pérez", "");
            Store.Employees.Add(employee.Id, employee);
            using var scope = Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateSponsorAsync(employee.Id);
            Assert.IsTrue(result.IsSuccess, result.Message);
            Store.Users[result.Value!.Account.Id].MustChangePassword = temporary;
            return result.Value;
        }
        internal async Task<CreatedAccount> Account(bool temporary = true, string role = AccountRoles.Administrator)
        {
            using var scope = Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IAccountService>()
                .CreateAdministrativeAsync("test-account", role);
            Assert.IsTrue(result.IsSuccess, result.Message);
            Store.Users[result.Value!.Account.Id].MustChangePassword = temporary;
            return result.Value;
        }
    }
    private sealed record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private static async Task<Tokens> Login(HttpClient client, CreatedAccount account, string? password = null)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account.Account.UserName, password = password ?? account.TemporaryPassword });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("no-store,no-cache", string.Join(',', response.Headers.CacheControl?.ToString().Split(", ") ?? []));
        var tokens = (await response.Content.ReadFromJsonAsync<Tokens>())!;
        Assert.AreEqual(900, tokens.ExpiresIn);
        return tokens;
    }
    private static void Bearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [TestMethod]
    public async Task ManualCampaignHttp_CreatedLocationDuplicateValidationAndPersistence()
    {
        using var host = new Host(); using var client = host.Client();
        Bearer(client, (await Login(client, await host.Account(false))).AccessToken);
        var body = new { evaluationMonth = "2020-10-01" };
        var response = await client.PostAsJsonAsync("/api/campaigns", body);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<CampaignDto>())!;
        Assert.IsNotNull(response.Headers.Location);
        Assert.IsTrue(response.Headers.Location.ToString().EndsWith($"/api/campaigns/{created.Id}"));
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/campaigns", body)).StatusCode);
        foreach (var invalid in new[] { "{}", "{\"evaluationMonth\":null}", "{\"evaluationMonth\":\"bad\"}",
            "{\"evaluationMonth\":\"2020-10-02\"}", "{\"evaluationMonth\":\"9999-01-01\"}" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/campaigns",
                new StringContent(invalid, System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        host.Campaigns.Failure = CampaignError.Persistence;
        var failure = await client.PostAsJsonAsync("/api/campaigns", new { evaluationMonth = "2020-09-01" });
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.IsFalse((await failure.Content.ReadAsStringAsync()).Contains("Internal"));
        Assert.AreEqual(1, host.Campaigns.Campaigns.Count);
    }

    [TestMethod]
    [DataRow(AccountRoles.HumanResources)]
    [DataRow(AccountRoles.Safety)]
    [DataRow(AccountRoles.Quality)]
    [DataRow(AccountRoles.Sponsor)]
    public async Task ManualCampaignHttp_RequiresAdministrator(string role)
    {
        using var host = new Host(); using var client = host.Client();
        var body = new { evaluationMonth = "2020-10-01" };
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/campaigns", body)).StatusCode);
        var account = role == AccountRoles.Sponsor ? await host.Sponsor() : await host.Account(false, role);
        Bearer(client, (await Login(client, account)).AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/campaigns", body)).StatusCode);
        Assert.AreEqual(0, host.Campaigns.Campaigns.Count);
    }

    private sealed class OpinionCampaignClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    }

    [TestMethod]
    [DataRow(AccountRoles.HumanResources)]
    [DataRow(AccountRoles.Safety)]
    [DataRow(AccountRoles.Quality)]
    public async Task OpinionCampaigns_DepartmentsReceiveMinimalOrderedPage(string role)
    {
        using var host = new Host(); using var client = host.Client();
        Bearer(client, (await Login(client, await host.Account(false, role))).AccessToken);
        host.Campaigns.Clock = new OpinionCampaignClock();
        var now = host.Campaigns.Clock.GetUtcNow();
        for (var month = 1; month <= 4; month++)
        {
            var campaign = new EmpleadoDelMes.Domain.Entities.Campaign(Guid.NewGuid(), new(2026, month, 1), now);
            if (month == 4) campaign.OpenOpinions();
            if (month == 3) { campaign.OpenOpinions(); campaign.Archive(); }
            if (month == 1)
            {
                campaign.ScheduleOpinions(now.AddHours(-1), now.AddHours(1));
                campaign.EnableAutomaticOpinions();
            }
            host.Campaigns.Campaigns.Add(campaign.EvaluationMonth, campaign);
        }
        var first = await client.GetFromJsonAsync<JsonElement>("/api/opinions/campaigns?pageSize=2");
        Assert.AreEqual(4, first.GetProperty("total").GetInt32());
        Assert.AreEqual(1, first.GetProperty("pageNumber").GetInt32());
        Assert.AreEqual(2, first.GetProperty("pageSize").GetInt32());
        var items = first.GetProperty("items");
        Assert.AreEqual(2, items.GetArrayLength());
        CollectionAssert.AreEquivalent(new[] { "id", "evaluationMonth", "canAcceptOpinions", "isArchived" },
            items[0].EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("2026-04-01", items[0].GetProperty("evaluationMonth").GetString());
        Assert.IsTrue(items[0].GetProperty("canAcceptOpinions").GetBoolean());
        Assert.IsTrue(items[1].GetProperty("isArchived").GetBoolean());
        Assert.IsFalse(items[1].GetProperty("canAcceptOpinions").GetBoolean());
        var second = await client.GetFromJsonAsync<JsonElement>("/api/opinions/campaigns?pageNumber=2&pageSize=2");
        Assert.AreEqual("2026-02-01", second.GetProperty("items")[0].GetProperty("evaluationMonth").GetString());
        Assert.IsFalse(second.GetProperty("items")[0].GetProperty("canAcceptOpinions").GetBoolean());
        Assert.IsTrue(second.GetProperty("items")[1].GetProperty("canAcceptOpinions").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/campaigns")).StatusCode);
    }

    [TestMethod]
    public async Task OpinionCampaigns_AnonymousSponsorAndAdministratorAreDenied()
    {
        using var host = new Host(); using var client = host.Client();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/opinions/campaigns")).StatusCode);
        Bearer(client, (await Login(client, await host.Sponsor())).AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/opinions/campaigns")).StatusCode);
        Bearer(client, (await Login(client, await host.Account(false))).AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/opinions/campaigns")).StatusCode);
    }

    [TestMethod]
    public async Task OpinionCampaigns_EnforcesCurrentAccountAndPasswordState()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account(true, AccountRoles.Safety);
        Bearer(client, (await Login(client, account)).AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/opinions/campaigns")).StatusCode);
        host.Store.Users[account.Account.Id].MustChangePassword = false;
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/opinions/campaigns")).StatusCode);
        host.Store.Memberships[account.Account.Id].Clear();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/opinions/campaigns")).StatusCode);
        host.Store.Users[account.Account.Id].IsActive = false;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/opinions/campaigns")).StatusCode);
    }

    [TestMethod]
    [DataRow(0, 20)]
    [DataRow(1, 0)]
    [DataRow(1, 101)]
    [DataRow(int.MaxValue, 100)]
    public async Task OpinionCampaigns_InvalidPaginationReturns400(int pageNumber, int pageSize)
    {
        using var host = new Host(); using var client = host.Client();
        Bearer(client, (await Login(client, await host.Account(false, AccountRoles.Quality))).AccessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/opinions/campaigns?pageNumber={pageNumber}&pageSize={pageSize}")).StatusCode);
    }

    [TestMethod]
    public async Task OpinionCampaigns_EmptyResponseIsSuccessful()
    {
        using var host = new Host(); using var client = host.Client();
        Bearer(client, (await Login(client, await host.Account(false, AccountRoles.HumanResources))).AccessToken);
        var response = await client.GetFromJsonAsync<JsonElement>("/api/opinions/campaigns");
        Assert.AreEqual(0, response.GetProperty("total").GetInt32());
        Assert.AreEqual(0, response.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task SponsorNominationHttp_CreatesUsingTrustedIdentity_AndReturns201()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Sponsor(); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        var candidate = new EmployeeDto(Guid.NewGuid(), "123", "Luis López", "private-version"); host.Store.Employees.Add(candidate.Id, candidate);
        var campaign = new EmpleadoDelMes.Domain.Entities.Campaign(Guid.NewGuid(), new(2026, 10, 1), DateTimeOffset.UtcNow);
        campaign.OpenNominations(); host.Campaigns.Campaigns.Add(campaign.EvaluationMonth, campaign);
        var active = await client.GetFromJsonAsync<JsonElement>("/api/nominations/active-campaign");
        Assert.AreEqual(campaign.Id, active.GetProperty("id").GetGuid());
        var search = await client.GetFromJsonAsync<JsonElement>("/api/nominations/candidates?term=Luis&pageSize=1");
        var row = search.GetProperty("items")[0];
        CollectionAssert.AreEquivalent(new[] { "id", "payrollNumber", "fullName" }, row.EnumerateObject().Select(p => p.Name).ToArray());
        var request = new { candidateEmployeeId = candidate.Id, qualities = 5, justification = "  Colabora con su equipo.  " };
        var response = await client.PostAsJsonAsync("/api/nominations", request);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(account.Account.Id, result.GetProperty("createdByUserId").GetGuid());
        Assert.AreEqual(account.Account.EmployeeId, result.GetProperty("sponsor").GetProperty("id").GetGuid());
        Assert.AreEqual("007", result.GetProperty("sponsor").GetProperty("payrollNumber").GetString());
        Assert.AreEqual("Colabora con su equipo.", result.GetProperty("justification").GetString());
        Assert.IsFalse(result.ToString().Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/nominations", request)).StatusCode);
        Assert.AreEqual(1, host.Nominations.Nominations.Count);
    }

    [TestMethod]
    [DataRow("sponsorEmployeeId")]
    [DataRow("sponsorPayrollNumber")]
    [DataRow("sponsorFullName")]
    [DataRow("createdByUserId")]
    [DataRow("campaignId")]
    public async Task NominationHttp_RejectsClientSuppliedIdentityAndCampaign(string field)
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Sponsor()); Bearer(client, tokens.AccessToken);
        var body = new Dictionary<string, object>
        {
            ["candidateEmployeeId"] = Guid.NewGuid(), ["qualities"] = 5, ["justification"] = "Texto válido", [field] = Guid.NewGuid().ToString()
        };
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/nominations", body)).StatusCode);
        Assert.AreEqual(0, host.Nominations.Nominations.Count);
    }

    [TestMethod]
    [DataRow(AccountRoles.Administrator)]
    [DataRow(AccountRoles.HumanResources)]
    [DataRow(AccountRoles.Safety)]
    [DataRow(AccountRoles.Quality)]
    public async Task NominationHttp_RequiresSponsorOnEveryEndpoint(string role)
    {
        using var host = new Host(); using var client = host.Client();
        var body = new { candidateEmployeeId = Guid.NewGuid(), qualities = 1, justification = "Texto" };
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/nominations/active-campaign")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/nominations/candidates?term=007")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/nominations", body)).StatusCode);
        var tokens = await Login(client, await host.Account(false, role)); Bearer(client, tokens.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/nominations/active-campaign")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/nominations/candidates?term=007")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/nominations", body)).StatusCode);
    }

    [TestMethod]
    public async Task NominationHttp_BlocksTemporaryAndDisabledSponsors()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Sponsor(true); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        var body = new { candidateEmployeeId = Guid.NewGuid(), qualities = 1, justification = "Texto" };
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/nominations", body)).StatusCode);
        host.Store.Users[account.Account.Id].MustChangePassword = false;
        host.Store.Users[account.Account.Id].IsActive = false;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/nominations", body)).StatusCode);
        Assert.AreEqual(0, host.Nominations.Nominations.Count);
    }

    [TestMethod]
    public async Task NominationHttp_ClosedCampaignAndPersistenceErrors_AreControlled()
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Sponsor()); Bearer(client, tokens.AccessToken);
        var candidate = new EmployeeDto(Guid.NewGuid(), "123", "Luis", ""); host.Store.Employees.Add(candidate.Id, candidate);
        var body = new { candidateEmployeeId = candidate.Id, qualities = 1, justification = "Texto" };
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.GetAsync("/api/nominations/active-campaign")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/nominations", body)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync("/api/nominations/candidates?term=Luis&pageSize=101")).StatusCode);
        var campaign = new EmpleadoDelMes.Domain.Entities.Campaign(Guid.NewGuid(), new(2026, 10, 1), DateTimeOffset.UtcNow);
        campaign.OpenNominations(); host.Campaigns.Campaigns.Add(campaign.EvaluationMonth, campaign);
        host.Nominations.FailSave = true;
        var response = await client.PostAsJsonAsync("/api/nominations", body);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("SQL"));
        Assert.AreEqual(0, host.Nominations.Nominations.Count);
    }

    [TestMethod]
    [DataRow("nominations/schedule", true)]
    [DataRow("opinions/schedule", true)]
    [DataRow("nominations/open", false)]
    [DataRow("nominations/close", false)]
    [DataRow("nominations/automatic", false)]
    [DataRow("opinions/open", false)]
    [DataRow("opinions/close", false)]
    [DataRow("opinions/automatic", false)]
    [DataRow("archive", false)]
    [DataRow("reopen", false)]
    public async Task CampaignMutations_RequireAdministratorAndChangedPassword(string action, bool put)
    {
        using var host = new Host(); using var client = host.Client();
        var path = $"/api/campaigns/{Guid.NewGuid()}/{action}";
        Task<HttpResponseMessage> Send() => put ? client.PutAsJsonAsync(path, new { }) : client.PostAsJsonAsync(path, new { });
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Send()).StatusCode);
        var account = await host.Account(true); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send()).StatusCode);
        host.Store.Users[account.Account.Id].MustChangePassword = false;
        host.Store.Memberships[account.Account.Id].Clear();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Send()).StatusCode);
    }

    [TestMethod]
    [DataRow(AccountRoles.Sponsor)]
    [DataRow(AccountRoles.HumanResources)]
    [DataRow(AccountRoles.Safety)]
    [DataRow(AccountRoles.Quality)]
    public async Task CampaignMutations_DenyEveryOtherFunctionalRole(string role)
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account(false); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>().CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
        host.Store.Memberships[account.Account.Id] = [role.ToUpperInvariant()];
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/campaigns/{Guid.NewGuid()}/archive", new { })).StatusCode);
    }

    [TestMethod]
    public async Task CampaignMutations_ScheduleUtc_UpdateVersion_AndReturnControlledErrors()
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Account(false)); Bearer(client, tokens.AccessToken);
        var campaign = new EmpleadoDelMes.Domain.Entities.Campaign(Guid.NewGuid(), new(2026, 10, 1), DateTimeOffset.UtcNow);
        host.Campaigns.Campaigns.Add(campaign.EvaluationMonth, campaign);
        var path = $"/api/campaigns/{campaign.Id}";
        var initial = await client.GetFromJsonAsync<JsonElement>(path);
        var version = initial.GetProperty("version").GetString();
        var scheduled = await client.PutAsJsonAsync(path + "/nominations/schedule", new
        {
            version, openingAt = "2026-11-03T06:00:00-06:00", closingAt = "2026-11-03T07:00:00-06:00"
        });
        Assert.AreEqual(HttpStatusCode.OK, scheduled.StatusCode, await scheduled.Content.ReadAsStringAsync());
        var current = await scheduled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero),
            current.GetProperty("nominationPeriod").GetProperty("scheduledOpeningAtUtc").GetDateTimeOffset());
        Assert.AreNotEqual(version, current.GetProperty("version").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/archive", new { version })).StatusCode);
        version = current.GetProperty("version").GetString();
        var opened = await client.PostAsJsonAsync(path + "/nominations/open", new { version });
        Assert.AreEqual(HttpStatusCode.OK, opened.StatusCode);
        current = await opened.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(current.GetProperty("canAcceptNominations").GetBoolean());
        version = current.GetProperty("version").GetString();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "/archive", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/campaigns/{Guid.NewGuid()}/archive", new { version })).StatusCode);
        host.Campaigns.FailSave = true;
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(path + "/archive", new { version })).StatusCode);
        var afterFailure = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.IsTrue(afterFailure.GetProperty("canAcceptNominations").GetBoolean());
        Assert.IsFalse(afterFailure.GetProperty("isArchived").GetBoolean());
    }

    [TestMethod]
    [DataRow("2026-11-03T06:00:00", "2026-11-03T07:00:00")]
    [DataRow("2026-11-03T06:00:00Z", "2026-11-03T07:00:00")]
    [DataRow("2026-11-03", "2026-11-04")]
    [DataRow("2026-11-03T06:00:00Z", "2026-11-03T06:00:00Z")]
    public async Task CampaignSchedule_InvalidOrMissingOffsets_Return400WithoutSaving(string openingAt, string closingAt)
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Account(false)); Bearer(client, tokens.AccessToken);
        var campaign = new EmpleadoDelMes.Domain.Entities.Campaign(Guid.NewGuid(), new(2026, 10, 1), DateTimeOffset.UtcNow);
        host.Campaigns.Campaigns.Add(campaign.EvaluationMonth, campaign);
        var before = host.Campaigns.ToDto(campaign);
        var response = await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}/opinions/schedule",
            new { before.Version, openingAt, closingAt });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(before, host.Campaigns.ToDto(host.Campaigns.Campaigns[campaign.EvaluationMonth]));
    }

    [TestMethod]
    [DataRow("/api/campaigns")]
    [DataRow("/api/campaigns/00000000-0000-0000-0000-000000000001")]
    public async Task CampaignEndpoints_RequireAdministratorAndChangedPassword(string path)
    {
        using var host = new Host(); using var client = host.Client();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        var account = await host.Account(true);
        var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        host.Store.Users[account.Account.Id].MustChangePassword = false;
        host.Store.Memberships[account.Account.Id].Clear();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [TestMethod]
    public async Task CampaignEndpoints_ReturnDetailsPaginationAndControlledErrors()
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Account(false)); Bearer(client, tokens.AccessToken);
        var campaign = new EmpleadoDelMes.Domain.Entities.Campaign(Guid.NewGuid(), new DateOnly(2026, 10, 1), DateTimeOffset.UtcNow);
        host.Campaigns.Campaigns.Add(campaign.EvaluationMonth, campaign);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/campaigns?pageNumber=1&pageSize=1");
        Assert.AreEqual(1, page.GetProperty("total").GetInt32());
        Assert.AreEqual("2026-10-01", page.GetProperty("items")[0].GetProperty("evaluationMonth").GetString());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/campaigns/{campaign.Id}");
        Assert.AreEqual(campaign.Id, detail.GetProperty("id").GetGuid());
        Assert.AreEqual(2, detail.GetProperty("nominationPeriod").GetProperty("mode").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, detail.GetProperty("opinionPeriod").GetProperty("scheduledOpeningAtUtc").ValueKind);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/campaigns/{Guid.NewGuid()}")).StatusCode);
        foreach (var path in new[] { "/api/campaigns/not-a-guid", $"/api/campaigns/{Guid.Empty}",
            "/api/campaigns?pageNumber=0", "/api/campaigns?pageSize=101", "/api/campaigns?pageNumber=invalid" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
        Assert.AreEqual(0, host.Campaigns.CreateCalls);
    }

    private static MultipartFormDataContent SponsorFile(bool confirm = false, string name = "sponsors.xlsx")
    {
        using var file = SponsorImportTests.Excel(("007", "Ana Pérez"));
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(file.ToArray()), "file", name);
        content.Add(new StringContent(confirm ? "true" : "false"), "confirm");
        return content;
    }

    [TestMethod]
    [DataRow("preview")]
    [DataRow("confirm")]
    public async Task SponsorImports_RequireAdministratorAndDefinitivePassword(string action)
    {
        using var host = new Host(); using var client = host.Client();
        using var file = SponsorFile(true);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/users/import/sponsors/{action}", file)).StatusCode);
        var account = await host.Account(true); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/users/import/sponsors/{action}", file)).StatusCode);
        host.Store.Users[account.Account.Id].MustChangePassword = false;
        host.Store.Memberships[account.Account.Id].Clear();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/users/import/sponsors/{action}", file)).StatusCode);
    }

    [TestMethod]
    public async Task SponsorImports_HttpPreviewConfirmAndIdempotence_DoNotDiscloseCredentials()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account(false); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        var employee = new EmployeeDto(Guid.NewGuid(), "007", "Ana Pérez", ""); host.Store.Employees.Add(employee.Id, employee);
        using var file = SponsorFile();
        var preview = await client.PostAsync("/api/users/import/sponsors/preview", file);
        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
        Assert.AreEqual(1, (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("newAccounts").GetInt32());
        Assert.AreEqual(1, host.Store.Users.Count);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/users/import/sponsors/confirm", file)).StatusCode);
        using var confirmed = SponsorFile(true);
        var response = await client.PostAsync("/api/users/import/sponsors/confirm", confirmed);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(1, json.GetProperty("createdAccounts").GetInt32());
        Assert.IsFalse(json.ToString().Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.ToString().Contains("token", StringComparison.OrdinalIgnoreCase));
        var second = await client.PostAsync("/api/users/import/sponsors/confirm", confirmed);
        Assert.AreEqual(1, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skippedAccounts").GetInt32());
        Assert.AreEqual(2, host.Store.Users.Count);
    }

    [TestMethod]
    public async Task SponsorImports_RejectWrongExtensionAndOversizedFile()
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Account(false)); Bearer(client, tokens.AccessToken);
        using var wrong = SponsorFile(name: "sponsors.csv");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/users/import/sponsors/preview", wrong)).StatusCode);
        using var oversized = new MultipartFormDataContent();
        oversized.Add(new ByteArrayContent(new byte[5 * 1024 * 1024 + 1]), "file", "sponsors.xlsx");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/users/import/sponsors/preview", oversized)).StatusCode);
    }

    [TestMethod]
    public async Task LoginAndMe_ReturnCurrentAccountWithoutSecrets()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { account.Account.UserName, password = account.TemporaryPassword });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("true", response.Headers.GetValues("X-Must-Change-Password").Single());
        var tokens = (await response.Content.ReadFromJsonAsync<Tokens>())!;
        Bearer(client, tokens.AccessToken);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.AreEqual(account.Account.Id, me.GetProperty("userId").GetGuid());
        Assert.IsTrue(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.IsFalse(me.ToString().Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(AccountRoles.Administrator, me.GetProperty("roles")[0].GetString());
    }

    [TestMethod]
    public async Task BadCredentialsAndLockedAccount_HaveSameResponseAndCountFailures()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account();
        var missing = await client.PostAsJsonAsync("/api/auth/login", new { userName = "missing", password = "incorrect" });
        var expected = await missing.Content.ReadAsStringAsync();
        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { account.Account.UserName, password = "incorrect" });
            Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.AreEqual(expected, await wrong.Content.ReadAsStringAsync());
        }
        Assert.IsTrue(host.Store.Users[account.Account.Id].LockoutEnd > DateTimeOffset.UtcNow);
        var locked = await client.PostAsJsonAsync("/api/auth/login", new { account.Account.UserName, password = account.TemporaryPassword });
        Assert.AreEqual(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.AreEqual(expected, await locked.Content.ReadAsStringAsync());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("invalid-token")]
    public async Task MissingOrInvalidAccess_IsUnauthorized(string? token)
    {
        using var host = new Host(); using var client = host.Client();
        if (token is not null) Bearer(client, token);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [TestMethod]
    public async Task ExpiredAccess_IsUnauthorized()
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Account(false));
        var options = host.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
        var ticket = options.BearerTokenProtector.Unprotect(tokens.AccessToken)!;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        Bearer(client, options.BearerTokenProtector.Protect(ticket));
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [TestMethod]
    public async Task TemporaryPassword_BlocksAllProtectedOperationsAndRefresh()
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Account()); Bearer(client, tokens.AccessToken);
        foreach (var route in new[] { "admin", "authenticated", "fallback" })
            Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync($"/test/authorization/{route}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken })).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [TestMethod]
    public async Task RolesAndTemporaryFlag_AreReadAgainForEveryRequest()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account(false);
        var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/test/authorization/admin")).StatusCode);
        host.Store.Memberships[account.Account.Id].Clear();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/test/authorization/admin")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/test/authorization/authenticated")).StatusCode);
        host.Store.Users[account.Account.Id].MustChangePassword = true;
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/test/authorization/authenticated")).StatusCode);
    }

    [TestMethod]
    public async Task PasswordChange_RevokesOldAccessAndRefresh_AndAllowsNewLogin()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account();
        var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = account.TemporaryPassword, newPassword = "Una frase nueva segura 2026" });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(host.Store.Users[account.Account.Id].MustChangePassword);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken })).StatusCode);
        var renewed = await Login(client, account, "Una frase nueva segura 2026"); Bearer(client, renewed.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/test/authorization/admin")).StatusCode);
    }

    [TestMethod]
    [DataRow("incorrect", "Una frase nueva segura 2026", 401)]
    [DataRow(null, "short", 400)]
    public async Task FailedPasswordChange_PreservesFlagAndSession(string? current, string next, int status)
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account(); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = current ?? account.TemporaryPassword, newPassword = next });
        Assert.AreEqual(status, (int)response.StatusCode);
        Assert.IsTrue(host.Store.Users[account.Account.Id].MustChangePassword);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [TestMethod]
    public async Task Refresh_UsesOfficialTicketsAndRejectsInvalidExpiredOrAccessTokens()
    {
        using var host = new Host(); using var client = host.Client();
        var tokens = await Login(client, await host.Account(false));
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken });
        Assert.AreEqual(HttpStatusCode.OK, refresh.StatusCode);
        var renewed = (await refresh.Content.ReadFromJsonAsync<Tokens>())!; Bearer(client, renewed.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var options = host.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
        var ticket = options.RefreshTokenProtector.Unprotect(tokens.RefreshToken)!;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        foreach (var token in new[] { "invalid", tokens.AccessToken, options.RefreshTokenProtector.Protect(ticket) })
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token })).StatusCode);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LockedOrDeletedAccount_InvalidatesAccessAndRefresh(bool locked)
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account(false); var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        if (locked) host.Store.Users[account.Account.Id].LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        else host.Store.Users.Remove(account.Account.Id);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken })).StatusCode);
    }

    [TestMethod]
    public async Task TechnicalEndpoints_RemainAnonymous_AndNoRegistrationIsPublished()
    {
        using var host = new Host(); using var client = host.Client();
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/ping")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var routes = host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToArray();
        Assert.IsFalse(routes.Any(r => r?.Contains("register") == true));
    }

    [TestMethod]
    public async Task PasswordChange_RequiresAuthentication_AndInvalidBodiesAreControlled()
    {
        using var host = new Host(); using var client = host.Client();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "current", newPassword = "new" })).StatusCode);
        foreach (var path in new[] { "login", "refresh" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/auth/{path}", new { })).StatusCode);
        var malformed = await client.PostAsync("/api/auth/login", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        StringAssert.Contains(await malformed.Content.ReadAsStringAsync(), "La solicitud no es válida");
    }

    [TestMethod]
    public async Task SponsorMe_ReturnsEmployeeDisplayNameAndPreservesPayroll()
    {
        using var host = new Host(); using var client = host.Client();
        var employee = new EmployeeDto(Guid.NewGuid(), "007", "Ana Pérez", "");
        host.Store.Employees.Add(employee.Id, employee);
        using var scope = host.Services.CreateScope();
        var account = (await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateSponsorAsync(employee.Id)).Value!;
        var tokens = await Login(client, account); Bearer(client, tokens.AccessToken);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.AreEqual("Ana Pérez", me.GetProperty("displayName").GetString());
        Assert.AreEqual("007", me.GetProperty("userName").GetString());
    }

    [TestMethod]
    [DataRow("Administrator", true)]
    [DataRow("Sponsor", false)]
    [DataRow("HumanResources", false)]
    [DataRow("Safety", false)]
    [DataRow("Quality", false)]
    public async Task Automation_RejectsTemporaryPasswordAndOtherRoles(string role, bool temporary)
    {
        using var host = new Host(); using var client = host.Client();
        var account = role == AccountRoles.Sponsor ? await host.Sponsor() : await host.Account(temporary, role);
        var tokens = await Login(client, account);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var get = await client.GetAsync("/api/admin/campaign-automation");
        Assert.AreEqual(HttpStatusCode.Forbidden, get.StatusCode);
        var put = await client.PutAsJsonAsync("/api/admin/campaign-automation",
            new AutomationUpdateRequest(true, AutomationTestStore.Calendar, Convert.ToBase64String(new byte[8])));
        Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode);
        var preview = await client.PostAsJsonAsync("/api/admin/campaign-automation/preview",
            new AutomationPreviewRequest(AutomationTestStore.Calendar));
        Assert.AreEqual(HttpStatusCode.Forbidden, preview.StatusCode);
    }

    [TestMethod]
    public async Task Automation_AnonymousRequiresSession()
    {
        using var host = new Host(); using var client = host.Client();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/campaign-automation")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/admin/campaign-automation",
            new AutomationUpdateRequest(true, AutomationTestStore.Calendar, ""))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/admin/campaign-automation/preview",
            new AutomationPreviewRequest(AutomationTestStore.Calendar))).StatusCode);
    }

    [TestMethod]
    public async Task Automation_AdministratorCanPreviewSaveAndReceiveConflictOrValidation()
    {
        using var host = new Host(); using var client = host.Client();
        var account = await host.Account(temporary: false);
        var tokens = await Login(client, account);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var state = (await client.GetFromJsonAsync<AutomationState>("/api/admin/campaign-automation"))!;
        Assert.IsFalse(state.Configuration.IsEnabled);
        var preview = await client.PostAsJsonAsync("/api/admin/campaign-automation/preview",
            new AutomationPreviewRequest(AutomationTestStore.Calendar));
        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
        var request = new AutomationUpdateRequest(true, AutomationTestStore.Calendar, state.Configuration.Version);
        var saved = await client.PutAsJsonAsync("/api/admin/campaign-automation", request);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        var updated = (await saved.Content.ReadFromJsonAsync<AutomationState>())!;
        Assert.IsTrue(updated.Configuration.IsEnabled);
        Assert.IsFalse(updated.EffectiveEnabled); // Technical switch is disabled in this test host.
        Assert.AreEqual(account.Account.Id, updated.Configuration.UpdatedByUserId);
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync("/api/admin/campaign-automation", request)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/admin/campaign-automation",
                request with { Calendar = null, Version = updated.Configuration.Version })).StatusCode);
    }}
