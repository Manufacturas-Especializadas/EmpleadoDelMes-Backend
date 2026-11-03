using ClosedXML.Excel;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Infrastructure;
using EmpleadoDelMes.Infrastructure.Employees;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Accounts;

internal sealed class SponsorTestDirectory(AccountTestStore store) : ISponsorImportDirectory
{
    public Task<SponsorImportSnapshot> ReadAsync(IReadOnlyList<string> payrolls, CancellationToken token) =>
        Task.FromResult(new SponsorImportSnapshot(
            store.Employees.Values.Select(e => new SponsorImportEmployee(e.Id, e.PayrollNumber, e.FullName)).ToArray(),
            store.Users.Values.Select(u => new SponsorImportAccount(u.EmployeeId, u.UserName!, u.IsActive,
                store.Memberships.GetValueOrDefault(u.Id, []).Select(n => store.Roles.Values.Single(r => r.NormalizedName == n).Name!).ToArray())).ToArray(),
            new HashSet<string>()));
}

[TestClass]
public sealed class SponsorImportTests
{
    internal static MemoryStream Excel(params (string Payroll, string Name)[] rows)
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Sponsors");
        sheet.Cell(1, 1).Value = "Nómina"; sheet.Cell(1, 2).Value = "Nombre completo";
        for (var i = 0; i < rows.Length; i++) { sheet.Cell(i + 2, 1).Value = rows[i].Payroll; sheet.Cell(i + 2, 2).Value = rows[i].Name; }
        var stream = new MemoryStream(); book.SaveAs(stream); stream.Position = 0; return stream;
    }
    private static ServiceProvider Provider(AccountTestStore store)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        services.AddSingleton<IUserStore<ApplicationUser>>(store);
        services.AddSingleton<IRoleStore<IdentityRole<Guid>>>(store);
        services.AddSingleton<IEmployeeRepository>(store);
        services.AddSingleton<IAccountTransactionRunner>(store);
        services.AddSingleton<ISponsorImportDirectory>(new SponsorTestDirectory(store));
        return services.BuildServiceProvider();
    }
    private static void Employee(AccountTestStore store, string payroll, string name = "Ana Pérez")
    {
        var employee = new EmployeeDto(Guid.NewGuid(), payroll, name, ""); store.Employees.Add(employee.Id, employee);
    }

    [TestMethod]
    public async Task PreviewAndConfirm_PreserveZeros_HashPayroll_AndAreIdempotent()
    {
        var store = new AccountTestStore(); Employee(store, "007"); Employee(store, "123"); Employee(store, "999");
        using var provider = Provider(store); var service = provider.GetRequiredService<SponsorImportService>();
        using var file = Excel(("007", " ANA   PEREZ "), ("123", "Ana Pérez"));
        var preview = await service.PreviewAsync(file);
        Assert.AreEqual(2, preview.Value!.NewAccounts); Assert.AreEqual(0, store.Users.Count);
        file.Position = 0; var result = await service.ConfirmAsync(file, true);
        Assert.IsTrue(result.IsSuccess, result.Message); Assert.AreEqual(2, result.Value!.CreatedAccounts);
        Assert.AreEqual(1, store.TransactionCalls);
        var manager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var user in store.Users.Values)
        {
            Assert.IsTrue(user.MustChangePassword && user.IsActive);
            Assert.AreNotEqual(user.UserName, user.PasswordHash);
            Assert.IsTrue(await manager.CheckPasswordAsync(user, user.UserName!));
            Assert.IsTrue(await manager.IsInRoleAsync(user, AccountRoles.Sponsor));
        }
        var hashes = store.Users.ToDictionary(p => p.Key, p => p.Value.PasswordHash);
        store.Users.Values.First().IsActive = false;
        file.Position = 0; var again = await service.ConfirmAsync(file, true);
        Assert.AreEqual(0, again.Value!.CreatedAccounts); Assert.AreEqual(2, again.Value.SkippedAccounts);
        Assert.AreEqual(2, store.Users.Count); Assert.IsFalse(store.Users.Values.First().IsActive);
        Assert.IsTrue(store.Users.All(p => p.Value.PasswordHash == hashes[p.Key]));
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(result).Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow(SponsorImportStatus.EmployeeNotFound)]
    [DataRow(SponsorImportStatus.NameMismatch)]
    [DataRow(SponsorImportStatus.DuplicatePayroll)]
    [DataRow(SponsorImportStatus.InvalidData)]
    public async Task ValidationErrors_RejectWholeFile(SponsorImportStatus expected)
    {
        var store = new AccountTestStore(); Employee(store, "007");
        using var provider = Provider(store); var service = provider.GetRequiredService<SponsorImportService>();
        var bad = expected switch
        {
            SponsorImportStatus.EmployeeNotFound => ("999", "Ana Pérez"),
            SponsorImportStatus.NameMismatch => ("007", "Otra persona"),
            SponsorImportStatus.DuplicatePayroll => ("007", "Ana Pérez"),
            _ => ("", "Ana Pérez")
        };
        using var file = Excel(("007", "Ana Pérez"), bad);
        var preview = await service.PreviewAsync(file);
        // Name mismatches share a payroll with the first row: use a single row to isolate the condition.
        if (expected == SponsorImportStatus.NameMismatch)
        {
            using var single = Excel(bad); preview = await service.PreviewAsync(single);
            single.Position = 0; Assert.AreEqual(AccountError.Validation, (await service.ConfirmAsync(single, true)).Error);
        }
        else { file.Position = 0; Assert.AreEqual(AccountError.Validation, (await service.ConfirmAsync(file, true)).Error); }
        Assert.IsTrue(preview.Value!.Rows.Any(r => r.Status == expected)); Assert.AreEqual(0, store.Users.Count);
    }

    [TestMethod]
    public async Task Confirmation_RevalidatesCatalog_AndRequiresExplicitConsent()
    {
        var store = new AccountTestStore(); Employee(store, "007"); using var provider = Provider(store);
        var service = provider.GetRequiredService<SponsorImportService>(); using var file = Excel(("007", "Ana Pérez"));
        Assert.AreEqual(AccountError.Validation, (await service.ConfirmAsync(file, false)).Error);
        Assert.AreEqual(0, store.TransactionCalls);
        Assert.AreEqual(1, (await service.PreviewAsync(file)).Value!.NewAccounts);
        store.Employees.Clear(); file.Position = 0;
        Assert.AreEqual(AccountError.Validation, (await service.ConfirmAsync(file, true)).Error);
        Assert.AreEqual(0, store.Users.Count);
    }

    [TestMethod]
    public async Task SecondWriteFailure_RollsBackFirstAccount_InSingleTransaction()
    {
        var store = new AccountTestStore { FailAssignmentAtUserCount = 2 }; Employee(store, "007"); Employee(store, "123");
        using var provider = Provider(store); using var file = Excel(("007", "Ana Pérez"), ("123", "Ana Pérez"));
        var result = await provider.GetRequiredService<SponsorImportService>().ConfirmAsync(file, true);
        Assert.AreEqual(AccountError.Persistence, result.Error); Assert.AreEqual(0, store.Users.Count);
        Assert.AreEqual(0, store.Memberships.Count); Assert.AreEqual(1, store.Rollbacks); Assert.AreEqual(1, store.TransactionCalls);
    }

    [TestMethod]
    [DataRow(true, true, AccountRoles.Sponsor, SponsorImportStatus.ExistingSponsor)]
    [DataRow(true, false, AccountRoles.Sponsor, SponsorImportStatus.InactiveSponsor)]
    [DataRow(true, true, AccountRoles.Administrator, SponsorImportStatus.AccountConflict)]
    [DataRow(false, true, AccountRoles.Sponsor, SponsorImportStatus.AccountConflict)]
    public void ExistingAccountStates_AreDistinguished(bool correctName, bool active, string role, SponsorImportStatus expected)
    {
        var id = Guid.NewGuid();
        var snapshot = new SponsorImportSnapshot([new(id, "007", "Ana Pérez")],
            [new(id, correctName ? "007" : "another", active, [role])], new HashSet<string>());
        var result = SponsorImportService.Validate([new(2, "007", "Ana Pérez", false)], snapshot);
        Assert.AreEqual(expected, result.Rows.Single().Status);
    }

    [TestMethod]
    public void ExcelReader_RejectsFormulaAndPreservesFormattedPayroll()
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet();
        sheet.Cell(1, 1).Value = "Nómina"; sheet.Cell(1, 2).Value = "Nombre completo";
        sheet.Cell(2, 1).Value = 7; sheet.Cell(2, 1).Style.NumberFormat.Format = "000"; sheet.Cell(2, 2).Value = "Ana";
        sheet.Cell(3, 1).FormulaA1 = "1+1"; sheet.Cell(3, 2).Value = "Ana";
        using var file = new MemoryStream(); book.SaveAs(file); file.Position = 0;
        var rows = new ClosedXmlEmployeeReader().Read(file);
        Assert.AreEqual("007", rows[0].PayrollNumber); Assert.IsTrue(rows[1].HasFormula);
        var result = SponsorImportService.Validate(rows, new([], [], new HashSet<string>()));
        Assert.AreEqual(SponsorImportStatus.InvalidData, result.Rows[1].Status);
    }

    [TestMethod]
    public void ExcelReader_RejectsMissingHeadersAndExcessRows()
    {
        using var file = Excel(Enumerable.Range(0, 1001).Select(i => (i.ToString(), "Ana")).ToArray());
        Assert.Throws<EmployeeExcelFormatException>(() => new ClosedXmlEmployeeReader().Read(file));
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet(); sheet.Cell(1, 1).Value = "Incorrecto"; sheet.Cell(2, 1).Value = "007";
        using var invalid = new MemoryStream(); book.SaveAs(invalid); invalid.Position = 0;
        Assert.Throws<EmployeeExcelFormatException>(() => new ClosedXmlEmployeeReader().Read(invalid));
    }
}
