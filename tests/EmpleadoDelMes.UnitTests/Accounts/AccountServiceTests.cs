using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Infrastructure;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Accounts;

[TestClass]
public sealed class AccountServiceTests
{
    private static ServiceProvider Provider(AccountTestStore store)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        services.AddSingleton<IUserStore<ApplicationUser>>(store);
        services.AddSingleton<IRoleStore<IdentityRole<Guid>>>(store);
        services.AddSingleton<IEmployeeRepository>(store);
        services.AddSingleton<IAccountTransactionRunner>(store);
        return services.BuildServiceProvider();
    }

    [TestMethod]
    [DataRow("123")]
    [DataRow("007")]
    public async Task Sponsor_ConservaNominaAsignaRolYNoDuplicaCuenta(string payroll)
    {
        var store = new AccountTestStore();
        var employee = new EmployeeDto(Guid.NewGuid(), payroll, "Ana", "");
        store.Employees.Add(employee.Id, employee);
        using var provider = Provider(store);
        var service = provider.GetRequiredService<IAccountService>();
        var result = await service.CreateSponsorAsync(employee.Id);
        Assert.IsTrue(result.IsSuccess);
        var created = result.Value!;
        Assert.AreEqual(payroll, created.Account.UserName);
        Assert.AreEqual(employee.Id, created.Account.EmployeeId);
        CollectionAssert.AreEqual(new[] { AccountRoles.Sponsor }, created.Account.Roles.ToArray());
        Assert.IsTrue(created.Account.MustChangePassword);
        Assert.AreEqual(payroll, created.TemporaryPassword);
        Assert.AreNotEqual(created.TemporaryPassword, store.Users[created.Account.Id].PasswordHash);
        Assert.IsTrue((await service.ValidateCredentialsAsync(payroll, created.TemporaryPassword)).IsSuccess);
        Assert.AreEqual(AccountError.Duplicate, (await service.CreateSponsorAsync(employee.Id)).Error);
        Assert.AreEqual(1, store.Users.Count);
        Assert.IsFalse((await service.GetAccountAsync(created.Account.Id)).Value!.ToString().Contains(created.TemporaryPassword));
    }

    [TestMethod]
    public async Task Sponsor_ExigeEmpleadoExistente()
    {
        using var provider = Provider(new AccountTestStore());
        var service = provider.GetRequiredService<IAccountService>();
        Assert.AreEqual(AccountError.Validation, (await service.CreateSponsorAsync(Guid.Empty)).Error);
        Assert.AreEqual(AccountError.NotFound, (await service.CreateSponsorAsync(Guid.NewGuid())).Error);
        Assert.AreEqual(AccountError.Validation, (await service.CreateAdministrativeAsync("007", AccountRoles.Sponsor)).Error);
    }

    [TestMethod]
    public async Task Sponsor_NoDuplicaVinculoAunqueLaCuentaTengaOtroNombre()
    {
        var store = new AccountTestStore();
        var employee = new EmployeeDto(Guid.NewGuid(), "007", "Ana", "");
        store.Employees.Add(employee.Id, employee);
        var existing = new ApplicationUser { Id = Guid.NewGuid(), EmployeeId = employee.Id,
            UserName = "nombre-anterior", NormalizedUserName = "NOMBRE-ANTERIOR" };
        store.Users.Add(existing.Id, existing);
        using var provider = Provider(store);
        var result = await provider.GetRequiredService<IAccountService>().CreateSponsorAsync(employee.Id);
        Assert.AreEqual(AccountError.Duplicate, result.Error);
        Assert.AreEqual(1, store.Users.Count);
        Assert.AreEqual("nombre-anterior", store.Users[existing.Id].UserName);
        Assert.AreEqual(employee.Id, store.Users[existing.Id].EmployeeId);
    }

    [TestMethod]
    public async Task AccesoCorrecto_ReiniciaIntentosYConsultaConservaRolesSinSecretos()
    {
        var store = new AccountTestStore();
        using var provider = Provider(store);
        var service = provider.GetRequiredService<IAccountService>();
        var created = (await service.CreateAdministrativeAsync("cuenta", AccountRoles.Quality)).Value!;
        await service.ValidateCredentialsAsync("cuenta", "incorrecta");
        Assert.AreEqual(1, store.Users[created.Account.Id].AccessFailedCount);
        var login = await service.ValidateCredentialsAsync("cuenta", created.TemporaryPassword);
        Assert.IsTrue(login.Value!.MustChangePassword);
        Assert.AreEqual("Debes cambiar tu contraseña antes de continuar.", login.Message);
        Assert.AreEqual(0, store.Users[created.Account.Id].AccessFailedCount);
        var account = (await service.GetAccountAsync(created.Account.Id)).Value!;
        CollectionAssert.AreEqual(new[] { AccountRoles.Quality }, account.Roles.ToArray());
        Assert.IsNull(typeof(AccountInfo).GetProperty("PasswordHash"));
        Assert.IsNull(typeof(AccountInfo).GetProperty("TemporaryPassword"));
        Assert.AreEqual(AccountError.NotFound, (await service.GetAccountAsync(Guid.NewGuid())).Error);
    }

    [TestMethod]
    [DataRow(AccountRoles.Administrator)]
    [DataRow(AccountRoles.HumanResources)]
    [DataRow(AccountRoles.Safety)]
    [DataRow(AccountRoles.Quality)]
    public async Task CuentaAdministrativa_NoRequiereEmpleado(string role)
    {
        using var provider = Provider(new AccountTestStore());
        var result = await provider.GetRequiredService<IAccountService>().CreateAdministrativeAsync("cuenta", role);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value!.Account.EmployeeId);
        Assert.IsTrue(result.Value.Account.MustChangePassword);
        CollectionAssert.AreEqual(new[] { role }, result.Value.Account.Roles.ToArray());
    }

    [TestMethod]
    public void Passwords_SonAleatoriasYPoliticaPermiteFrases()
    {
        var generator = new TemporaryPasswordGenerator();
        var passwords = Enumerable.Range(0, 100).Select(_ => generator.Generate()).ToArray();
        Assert.AreEqual(100, passwords.Distinct().Count());
        Assert.IsTrue(passwords.All(p => p.Length == 24));
        using var provider = Provider(new AccountTestStore());
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.AreEqual(12, options.Password.RequiredLength);
        Assert.IsFalse(options.Password.RequireNonAlphanumeric);
        Assert.AreEqual(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.AreEqual(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
    }

    [TestMethod]
    public async Task Credenciales_IdentityCuentaFallosBloqueaYNoRevelaExistencia()
    {
        var store = new AccountTestStore();
        using var provider = Provider(store);
        var service = provider.GetRequiredService<IAccountService>();
        var created = (await service.CreateAdministrativeAsync("cuenta", AccountRoles.Quality)).Value!;
        var missing = await service.ValidateCredentialsAsync("inexistente", "incorrecta");
        for (var i = 0; i < 5; i++)
        {
            var failed = await service.ValidateCredentialsAsync("cuenta", "incorrecta");
            Assert.AreEqual(missing.Error, failed.Error);
            Assert.AreEqual(missing.Message, failed.Message);
        }
        Assert.IsTrue(store.Users[created.Account.Id].LockoutEnd > DateTimeOffset.UtcNow);
        var locked = await service.ValidateCredentialsAsync("cuenta", created.TemporaryPassword);
        Assert.AreEqual(missing, locked);
        Assert.IsTrue(store.Users[created.Account.Id].MustChangePassword);
        store.Users[created.Account.Id].LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.IsTrue((await service.ValidateCredentialsAsync("cuenta", created.TemporaryPassword)).IsSuccess);
    }

    [TestMethod]
    public async Task CambioExitoso_RenuevaStampYQuitaObligacion()
    {
        var store = new AccountTestStore();
        using var provider = Provider(store);
        var service = provider.GetRequiredService<IAccountService>();
        var created = (await service.CreateAdministrativeAsync("cuenta", AccountRoles.Safety)).Value!;
        var stamp = store.Users[created.Account.Id].SecurityStamp;
        var changed = await service.ChangePasswordAsync(created.Account.Id, created.TemporaryPassword, "una frase larga para mi cuenta");
        Assert.IsTrue(changed.IsSuccess);
        Assert.IsFalse(changed.Value!.MustChangePassword);
        Assert.AreNotEqual(stamp, store.Users[created.Account.Id].SecurityStamp);
        Assert.IsFalse((await service.ValidateCredentialsAsync("cuenta", created.TemporaryPassword)).IsSuccess);
        Assert.IsTrue((await service.ValidateCredentialsAsync("cuenta", "una frase larga para mi cuenta")).IsSuccess);
    }

    [TestMethod]
    public async Task CambioFallido_ConservaPasswordYObligacion()
    {
        var store = new AccountTestStore();
        using var provider = Provider(store);
        var service = provider.GetRequiredService<IAccountService>();
        var created = (await service.CreateAdministrativeAsync("cuenta", AccountRoles.Safety)).Value!;
        Assert.IsFalse((await service.ChangePasswordAsync(created.Account.Id, "incorrecta", "una frase suficientemente larga")).IsSuccess);
        Assert.AreEqual(1, store.Users[created.Account.Id].AccessFailedCount);
        Assert.IsFalse((await service.ChangePasswordAsync(created.Account.Id, created.TemporaryPassword, "corta")).IsSuccess);
        Assert.IsFalse((await service.ChangePasswordAsync(created.Account.Id, created.TemporaryPassword, "aaaaaaaaaaaa")).IsSuccess);
        Assert.IsTrue(store.Users[created.Account.Id].MustChangePassword);
        Assert.IsTrue((await service.ValidateCredentialsAsync("cuenta", created.TemporaryPassword)).IsSuccess);
    }

    [TestMethod]
    public async Task FalloAsignacion_NoDejaCuentaNiRolParcial()
    {
        var store = new AccountTestStore { FailAssignment = true };
        using var provider = Provider(store);
        var result = await provider.GetRequiredService<IAccountService>().CreateAdministrativeAsync("cuenta", AccountRoles.Administrator);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, store.Users.Count);
        Assert.AreEqual(0, store.Roles.Count);
        Assert.AreEqual(1, store.Rollbacks);
    }

    [TestMethod]
    public async Task FalloAlGuardarFlag_ReviertePasswordYStamp()
    {
        var store = new AccountTestStore();
        using var provider = Provider(store);
        var service = provider.GetRequiredService<IAccountService>();
        var created = (await service.CreateAdministrativeAsync("cuenta", AccountRoles.Administrator)).Value!;
        var hash = store.Users[created.Account.Id].PasswordHash;
        var stamp = store.Users[created.Account.Id].SecurityStamp;
        store.FailFlagUpdate = true;
        var result = await service.ChangePasswordAsync(created.Account.Id, created.TemporaryPassword, "una frase suficientemente larga");
        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(store.Users[created.Account.Id].MustChangePassword);
        Assert.AreEqual(hash, store.Users[created.Account.Id].PasswordHash);
        Assert.AreEqual(stamp, store.Users[created.Account.Id].SecurityStamp);
    }
}
