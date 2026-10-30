using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Infrastructure;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests.Accounts;

[TestClass]
public sealed class AdminBootstrapTests
{
    // Models serialization and atomic rollback, not SQL Server's physical application lock.
    private sealed class Transaction(AccountTestStore store) : IAdminBootstrapTransaction
    {
        private readonly SemaphoreSlim gate = new(1);
        public async Task<bool> RunAsync(Func<Task<bool>> operation, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await Task.Yield();
                return (await store.RunAsync(async () => new AccountResult<bool>(await operation()), cancellationToken)).Value;
            }
            finally { gate.Release(); }
        }
    }

    private static ServiceProvider Provider(AccountTestStore store, string? enabled = "true", string? name = "initial-admin", string? password = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminBootstrap:Enabled"] = enabled,
            ["AdminBootstrap:UserName"] = name,
            ["AdminBootstrap:Password"] = password
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        services.AddSingleton<IUserStore<ApplicationUser>>(store);
        services.AddSingleton<IRoleStore<IdentityRole<Guid>>>(store);
        services.AddSingleton<IAdminBootstrapTransaction>(new Transaction(store));
        return services.BuildServiceProvider();
    }

    private static Task Start(ServiceProvider provider) => provider.GetRequiredService<AdminBootstrapStartup>()
        .StartAsync(CancellationToken.None);

    [TestMethod]
    [DataRow(null)]
    [DataRow("false")]
    public async Task Disabled_DoesNotResolveIdentityOrPersistence(string? enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AdminBootstrap:Enabled"] = enabled }).Build());
        services.AddScoped<AdminBootstrapInitializer>(_ => throw new AssertFailedException("Initializer must not be resolved"));
        services.AddSingleton<AdminBootstrapStartup>();
        using var provider = services.BuildServiceProvider();
        await Start(provider);
    }

    [TestMethod]
    public async Task Enabled_CreatesFiveRolesAndHashedTemporaryAdministrator_OnlyOnce()
    {
        var store = new AccountTestStore(); var password = new TemporaryPasswordGenerator().Generate();
        using (var provider = Provider(store, password: password))
        {
            await Start(provider);
            using var scope = provider.CreateScope();
            var user = store.Users.Values.Single();
            Assert.AreEqual(5, store.Roles.Count);
            Assert.IsTrue(user.MustChangePassword);
            Assert.IsNull(user.EmployeeId);
            Assert.AreNotEqual(password, user.PasswordHash);
            Assert.IsTrue(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CheckPasswordAsync(user, password));
            Assert.IsTrue(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().IsInRoleAsync(user, AccountRoles.Administrator));
        }
        var hash = store.Users.Values.Single().PasswordHash;
        using var restart = Provider(store, name: "different-admin", password: new TemporaryPasswordGenerator().Generate());
        await Start(restart);
        Assert.AreEqual(1, store.Users.Count);
        Assert.AreEqual(hash, store.Users.Values.Single().PasswordHash);
    }

    [TestMethod]
    public async Task ExistingAdministrator_IsNotModified_AndDeletionDoesNotReenableBootstrap()
    {
        var store = new AccountTestStore();
        using var provider = Provider(store, password: new TemporaryPasswordGenerator().Generate());
        using (var scope = provider.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await roles.CreateAsync(new IdentityRole<Guid>(AccountRoles.Administrator) { Id = Guid.NewGuid() });
            var existing = new ApplicationUser { Id = Guid.NewGuid(), UserName = "existing", MustChangePassword = false };
            await users.CreateAsync(existing, new TemporaryPasswordGenerator().Generate());
            await users.AddToRoleAsync(existing, AccountRoles.Administrator);
        }
        var hash = store.Users.Values.Single().PasswordHash;
        await Start(provider);
        Assert.AreEqual(1, store.Roles.Count);
        Assert.AreEqual(hash, store.Users.Values.Single().PasswordHash);
        Assert.IsFalse(store.Users.Values.Single().MustChangePassword);
        store.Users.Clear(); store.Memberships.Clear();
        using var restart = Provider(store, password: new TemporaryPasswordGenerator().Generate());
        await Start(restart);
        Assert.AreEqual(0, store.Users.Count);
    }

    [TestMethod]
    public async Task CreatedAdministratorDeletion_DoesNotReenableBootstrap()
    {
        var store = new AccountTestStore();
        using (var provider = Provider(store, password: new TemporaryPasswordGenerator().Generate())) await Start(provider);
        store.Users.Clear(); store.Memberships.Clear();
        using var restart = Provider(store, password: new TemporaryPasswordGenerator().Generate());
        await Start(restart);
        Assert.AreEqual(0, store.Users.Count);
    }

    [TestMethod]
    [DataRow("true", null, null)]
    [DataRow("true", "admin", null)]
    [DataRow("true", "admin", "short")]
    [DataRow("true", "invalid name", "not-used")]
    [DataRow("invalid", null, null)]
    public async Task InvalidConfiguration_FailsSafelyWithoutChanges(string enabled, string? name, string? password)
    {
        var store = new AccountTestStore(); using var provider = Provider(store, enabled, name, password);
        await Assert.ThrowsAsync<AdminBootstrapException>(() => Start(provider));
        Assert.AreEqual(0, store.Users.Count);
        Assert.AreEqual(0, store.Roles.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AssignmentOrMarkerFailure_RollsBackAndDoesNotExposeDetails(bool marker)
    {
        var store = new AccountTestStore { FailAssignment = !marker, FailMarker = marker };
        var password = new TemporaryPasswordGenerator().Generate();
        using var provider = Provider(store, password: password);
        var exception = await Assert.ThrowsAsync<AdminBootstrapException>(() => Start(provider));
        Assert.IsNull(exception.InnerException);
        Assert.IsFalse(exception.ToString().Contains(password));
        Assert.IsFalse(exception.ToString().Contains("Sensitive provider detail"));
        Assert.AreEqual(0, store.Users.Count);
        Assert.AreEqual(0, store.Roles.Count);
        Assert.AreEqual(0, store.RoleClaims.Count);
        Assert.AreEqual(1, store.Rollbacks);
    }

    [TestMethod]
    public async Task ConcurrentInitializers_CreateExactlyOneAdministrator()
    {
        var store = new AccountTestStore(); using var provider = Provider(store);
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var password = new TemporaryPasswordGenerator().Generate();
        var results = await Task.WhenAll(
            first.ServiceProvider.GetRequiredService<AdminBootstrapInitializer>().InitializeAsync("first", password, CancellationToken.None),
            second.ServiceProvider.GetRequiredService<AdminBootstrapInitializer>().InitializeAsync("second", password, CancellationToken.None));
        Assert.AreEqual(1, results.Count(x => x));
        Assert.AreEqual(1, store.Users.Count);
        Assert.AreEqual(5, store.Roles.Count);
    }

    [TestMethod]
    public async Task ExistingNonAdministrator_IsNeverElevatedOrReset()
    {
        var store = new AccountTestStore(); using var provider = Provider(store, password: new TemporaryPasswordGenerator().Generate());
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.CreateAsync(new ApplicationUser { Id = Guid.NewGuid(), UserName = "initial-admin" }, new TemporaryPasswordGenerator().Generate());
        var hash = store.Users.Values.Single().PasswordHash;
        await Assert.ThrowsAsync<AdminBootstrapException>(() => Start(provider));
        Assert.AreEqual(hash, store.Users.Values.Single().PasswordHash);
        Assert.AreEqual(0, store.Roles.Count);
    }
}
