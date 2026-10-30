using System.Text.Json;
using System.Security.Claims;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace EmpleadoDelMes.UnitTests.Accounts;

// Almacenes de prueba para ejecutar los UserManager/RoleManager reales sin SQL Server.
internal sealed class AccountTestStore : IUserPasswordStore<ApplicationUser>, IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>, IUserRoleStore<ApplicationUser>, IRoleClaimStore<IdentityRole<Guid>>,
    IEmployeeRepository, IAccountTransactionRunner
{
    internal Dictionary<Guid, ApplicationUser> Users = [];
    internal Dictionary<Guid, IdentityRole<Guid>> Roles = [];
    internal Dictionary<Guid, List<string>> Memberships = [];
    internal Dictionary<Guid, EmployeeDto> Employees = [];
    internal Dictionary<Guid, List<(string Type, string Value)>> RoleClaims = [];
    internal bool FailMarker;
    internal bool FailAssignment;
    internal bool FailFlagUpdate;
    internal int Rollbacks;
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static IdentityResult Failure() => IdentityResult.Failed(new IdentityError { Code = "StoreFailure", Description = "Detalle interno" });
    public void Dispose() { }
    public Task<string> GetUserIdAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.Id.ToString());
    public Task<string?> GetUserNameAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.UserName);
    public Task SetUserNameAsync(ApplicationUser u, string? n, CancellationToken c) { u.UserName = n; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser u, string? n, CancellationToken c) { u.NormalizedUserName = n; return Task.CompletedTask; }
    public Task<IdentityResult> CreateAsync(ApplicationUser u, CancellationToken c)
    {
        if (Users.Values.Any(x => x.NormalizedUserName == u.NormalizedUserName || u.EmployeeId != null && x.EmployeeId == u.EmployeeId))
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "DuplicateEmployee" }));
        Users.Add(u.Id, Clone(u));
        return Task.FromResult(IdentityResult.Success);
    }
    public Task<IdentityResult> UpdateAsync(ApplicationUser u, CancellationToken c)
    {
        if (FailFlagUpdate && !u.MustChangePassword) return Task.FromResult(Failure());
        Users[u.Id] = Clone(u);
        return Task.FromResult(IdentityResult.Success);
    }
    public Task<IdentityResult> DeleteAsync(ApplicationUser u, CancellationToken c) => throw new NotSupportedException();
    public Task<ApplicationUser?> FindByIdAsync(string id, CancellationToken c) => Task.FromResult(Users.TryGetValue(Guid.Parse(id), out var user) ? Clone(user) : null);
    public Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken c) => Task.FromResult(Users.Values.Where(u => u.NormalizedUserName == name).Select(Clone).SingleOrDefault());
    public Task SetPasswordHashAsync(ApplicationUser u, string? hash, CancellationToken c) { u.PasswordHash = hash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.PasswordHash);
    public Task<bool> HasPasswordAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.PasswordHash != null);
    public Task SetSecurityStampAsync(ApplicationUser u, string stamp, CancellationToken c) { u.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.SecurityStamp);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.LockoutEnd);
    public Task SetLockoutEndDateAsync(ApplicationUser u, DateTimeOffset? end, CancellationToken c) { u.LockoutEnd = end; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(++u.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(ApplicationUser u, CancellationToken c) { u.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(ApplicationUser u, CancellationToken c) => Task.FromResult(u.LockoutEnabled);
    public Task SetLockoutEnabledAsync(ApplicationUser u, bool enabled, CancellationToken c) { u.LockoutEnabled = enabled; return Task.CompletedTask; }
    public Task AddToRoleAsync(ApplicationUser u, string role, CancellationToken c)
    {
        if (FailAssignment) throw new Microsoft.EntityFrameworkCore.DbUpdateException("Detalle interno");
        if (!Memberships.TryGetValue(u.Id, out var list)) Memberships[u.Id] = list = [];
        list.Add(role);
        return Task.CompletedTask;
    }
    public Task RemoveFromRoleAsync(ApplicationUser u, string role, CancellationToken c) => throw new NotSupportedException();
    public Task<IList<string>> GetRolesAsync(ApplicationUser u, CancellationToken c) => Task.FromResult<IList<string>>(
        Memberships.GetValueOrDefault(u.Id, []).Select(n => Roles.Values.Single(r => r.NormalizedName == n).Name!).ToList());
    public Task<bool> IsInRoleAsync(ApplicationUser u, string role, CancellationToken c) => Task.FromResult(Memberships.GetValueOrDefault(u.Id, []).Contains(role));
    public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string role, CancellationToken c) =>
        Task.FromResult<IList<ApplicationUser>>(Users.Values.Where(u => Memberships.GetValueOrDefault(u.Id, []).Contains(role)).Select(Clone).ToList());
    public Task<string> GetRoleIdAsync(IdentityRole<Guid> r, CancellationToken c) => Task.FromResult(r.Id.ToString());
    public Task<string?> GetRoleNameAsync(IdentityRole<Guid> r, CancellationToken c) => Task.FromResult(r.Name);
    public Task SetRoleNameAsync(IdentityRole<Guid> r, string? n, CancellationToken c) { r.Name = n; return Task.CompletedTask; }
    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole<Guid> r, CancellationToken c) => Task.FromResult(r.NormalizedName);
    public Task SetNormalizedRoleNameAsync(IdentityRole<Guid> r, string? n, CancellationToken c) { r.NormalizedName = n; return Task.CompletedTask; }
    public Task<IdentityResult> CreateAsync(IdentityRole<Guid> r, CancellationToken c) { Roles.Add(r.Id, Clone(r)); return Task.FromResult(IdentityResult.Success); }
    public Task<IdentityResult> UpdateAsync(IdentityRole<Guid> r, CancellationToken c) { Roles[r.Id] = Clone(r); return Task.FromResult(IdentityResult.Success); }
    public Task<IdentityResult> DeleteAsync(IdentityRole<Guid> r, CancellationToken c) => throw new NotSupportedException();
    Task<IdentityRole<Guid>?> IRoleStore<IdentityRole<Guid>>.FindByIdAsync(string id, CancellationToken c) => Task.FromResult(Roles.GetValueOrDefault(Guid.Parse(id)));
    Task<IdentityRole<Guid>?> IRoleStore<IdentityRole<Guid>>.FindByNameAsync(string name, CancellationToken c) => Task.FromResult(Roles.Values.SingleOrDefault(r => r.NormalizedName == name));
    public Task<IList<Claim>> GetClaimsAsync(IdentityRole<Guid> r, CancellationToken c) =>
        Task.FromResult<IList<Claim>>(RoleClaims.GetValueOrDefault(r.Id, []).Select(x => new Claim(x.Type, x.Value)).ToList());
    public Task AddClaimAsync(IdentityRole<Guid> r, Claim claim, CancellationToken c)
    {
        if (FailMarker) throw new InvalidOperationException("Sensitive provider detail");
        if (!RoleClaims.TryGetValue(r.Id, out var claims)) RoleClaims[r.Id] = claims = [];
        claims.Add((claim.Type, claim.Value));
        return Task.CompletedTask;
    }
    public Task RemoveClaimAsync(IdentityRole<Guid> r, Claim claim, CancellationToken c) => throw new NotSupportedException();
    public Task<EmployeeDto?> GetByIdAsync(Guid id, CancellationToken c) => Task.FromResult(Employees.GetValueOrDefault(id));
    public Task<EmployeeDto?> GetByPayrollNumberAsync(string number, CancellationToken c) => throw new NotSupportedException();
    public Task<EmployeePage> SearchAsync(string term, int page, int size, CancellationToken c) => throw new NotSupportedException();
    public Task<EmployeeDto> CreateAsync(Employee employee, CancellationToken c) => throw new NotSupportedException();
    public Task<EmployeeDto> UpdateNameAsync(Employee employee, byte[] version, CancellationToken c) => throw new NotSupportedException();
    public async Task<AccountResult<T>> RunAsync<T>(Func<Task<AccountResult<T>>> operation, CancellationToken c)
    {
        var users = Clone(Users); var roles = Clone(Roles); var memberships = Clone(Memberships);
        var roleClaims = RoleClaims.ToDictionary(p => p.Key, p => p.Value.ToList());
        var success = false;
        try { var result = await operation(); success = result.IsSuccess; return result; }
        finally { if (!success) { Users = users; Roles = roles; Memberships = memberships; RoleClaims = roleClaims; Rollbacks++; } }
    }
}
