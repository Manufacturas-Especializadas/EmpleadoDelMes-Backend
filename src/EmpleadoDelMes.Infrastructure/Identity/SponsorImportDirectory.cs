using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Identity;

public sealed class SponsorImportDirectory(ApplicationDbContext context, UserManager<ApplicationUser> users) : ISponsorImportDirectory
{
    public async Task<SponsorImportSnapshot> ReadAsync(IReadOnlyList<string> payrolls, CancellationToken cancellationToken)
    {
        var employees = new List<SponsorImportEmployee>();
        var accounts = new Dictionary<Guid, SponsorImportAccount>();
        foreach (var batch in payrolls.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
        {
            var found = await context.Employees.AsNoTracking().Where(e => batch.Contains(e.PayrollNumber))
                .Select(e => new SponsorImportEmployee(e.Id, e.PayrollNumber, e.FullName)).ToArrayAsync(cancellationToken);
            employees.AddRange(found);
            var ids = found.Select(e => e.Id).ToArray();
            var names = batch.Select(users.NormalizeName).ToArray();
            var matching = await context.Users.AsNoTracking()
                .Where(u => (u.EmployeeId.HasValue && ids.Contains(u.EmployeeId.Value)) || names.Contains(u.NormalizedUserName))
                .Select(u => new { u.Id, u.EmployeeId, u.UserName, u.IsActive }).ToArrayAsync(cancellationToken);
            var accountIds = matching.Select(u => u.Id).ToArray();
            var assigned = await (from ur in context.UserRoles.AsNoTracking()
                                  join role in context.Roles.AsNoTracking() on ur.RoleId equals role.Id
                                  where accountIds.Contains(ur.UserId)
                                  select new { ur.UserId, role.Name }).ToArrayAsync(cancellationToken);
            foreach (var account in matching)
                accounts[account.Id] = new(account.EmployeeId, account.UserName!, account.IsActive,
                    assigned.Where(r => r.UserId == account.Id).Select(r => r.Name!).ToArray());
        }
        var allowed = users.Options.User.AllowedUserNameCharacters;
        var invalid = payrolls.Where(n => !string.IsNullOrEmpty(allowed) && n.Any(c => !allowed.Contains(c)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(employees.DistinctBy(e => e.Id).ToArray(), accounts.Values.ToArray(), invalid);
    }
}
