using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EmpleadoDelMes.Application.Accounts;
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
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // Keep test diagnostics in-process; never write to the Windows Event Log.
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["AdminBootstrap:Enabled"] = "false", ["CampaignAutomation:Enabled"] = "false" }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IUserStore<ApplicationUser>>(Store);
                services.AddSingleton<IRoleStore<IdentityRole<Guid>>>(Store);
                services.AddSingleton<IEmployeeRepository>(Store);
                services.AddSingleton<IAccountTransactionRunner>(Store);
                services.AddSingleton<ICampaignRepository>(Campaigns);
                services.AddSingleton<ISponsorImportDirectory>(new SponsorTestDirectory(Store));
                services.AddControllers().AddApplicationPart(typeof(AuthorizationProbeController).Assembly);
            });
        }
        internal HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
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
}
