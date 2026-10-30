
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Identity;

public sealed class AccountDirectory(
    ApplicationDbContext context) : IAccountDirectory
{
    public async Task<AccountResult<AccountPage>> SearchAsync(
        string? term,
        string? role,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Validar la paginación.
        if (pageNumber < 1 ||
            pageSize < 1 ||
            pageSize > 100 ||
            (long)(pageNumber - 1) * pageSize > int.MaxValue)
        {
            return Invalid(
                "Indica una página válida y un tamaño entre 1 y 100.");
        }

        var search = term?.Trim() ?? "";

        if (search.Length > 100)
        {
            return Invalid(
                "La búsqueda no puede superar los 100 caracteres.");
        }

        // Normalizar el filtro de rol.
        var requestedRole = role?.Trim();

        string[] allowedRoles =
        [
            AccountRoles.Administrator,
            AccountRoles.Sponsor,
            AccountRoles.HumanResources,
            AccountRoles.Safety,
            AccountRoles.Quality
        ];

        var selectedRole = allowedRoles.FirstOrDefault(
            r => string.Equals(
                r,
                requestedRole,
                StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(requestedRole) &&
            selectedRole is null)
        {
            return Invalid("Selecciona un rol válido.");
        }

        try
        {
            var query = context.Users.AsNoTracking();

            // Buscar por usuario, nómina o nombre del empleado.
            if (search.Length > 0)
            {
                query = query.Where(user =>
                    user.UserName!.Contains(search) ||
                    context.Employees.Any(employee =>
                        employee.Id == user.EmployeeId &&
                        (employee.PayrollNumber.Contains(search) ||
                         employee.FullName.Contains(search))));
            }

            // Filtrar por rol.
            if (selectedRole is not null)
            {
                query = query.Where(user =>
                    context.UserRoles.Any(userRole =>
                        userRole.UserId == user.Id &&
                        context.Roles.Any(r =>
                            r.Id == userRole.RoleId &&
                            r.Name == selectedRole)));
            }

            // Contar los resultados antes de paginar.
            var total = await query.CountAsync(cancellationToken);

            // Obtener únicamente la página solicitada.
            var accounts = await query
                .OrderBy(user => user.UserName)
                .ThenBy(user => user.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(user => new
                {
                    user.Id,
                    UserName = user.UserName!,
                    user.EmployeeId,
                    user.MustChangePassword,
                    EmployeeFullName = context.Employees
                        .Where(employee =>
                            employee.Id == user.EmployeeId)
                        .Select(employee => employee.FullName)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            // Consultar los roles de los usuarios de esta página.
            var accountIds = accounts
                .Select(account => account.Id)
                .ToArray();

            var accountRoles = await (
                from userRole in context.UserRoles.AsNoTracking()
                join identityRole in context.Roles.AsNoTracking()
                    on userRole.RoleId equals identityRole.Id
                where accountIds.Contains(userRole.UserId)
                select new
                {
                    userRole.UserId,
                    Role = identityRole.Name!
                }
            ).ToListAsync(cancellationToken);

            var rolesByUser = accountRoles.ToLookup(
                item => item.UserId,
                item => item.Role);

            var items = accounts.Select(account =>
                new AccountListItem(
                    account.Id,
                    account.UserName,
                    account.EmployeeId,
                    account.EmployeeFullName,
                    account.MustChangePassword,
                    rolesByUser[account.Id]
                        .OrderBy(r => r)
                        .ToArray()
                )
            ).ToArray();

            var page = new AccountPage(
                items,
                total,
                pageNumber,
                pageSize);

            return new AccountResult<AccountPage>(page);
        }
        catch (SqlException)
        {
            return StorageFailure();
        }
        catch (DbUpdateException)
        {
            return StorageFailure();
        }
    }

    private static AccountResult<AccountPage> Invalid(
        string message)
    {
        return new AccountResult<AccountPage>(
            null,
            AccountError.Validation,
            message);
    }

    private static AccountResult<AccountPage> StorageFailure()
    {
        return new AccountResult<AccountPage>(
            null,
            AccountError.Persistence,
            "No pudimos consultar los usuarios. Inténtalo nuevamente.");
    }
}
