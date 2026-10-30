using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Identity;

public sealed class IdentityAccountService(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IEmployeeRepository employees,
    IAccountTransactionRunner transactions,
    TemporaryPasswordGenerator passwords) : IAccountService
{
    private const string InvalidLogin = "El usuario o la contraseña son incorrectos, o la cuenta está bloqueada temporalmente.";
    private static AccountResult<T> Fail<T>(AccountError error, string message) => new(default, error, message);
    private static AccountResult<T> Denied<T>() => Fail<T>(AccountError.InvalidCredentials, InvalidLogin);

    public Task<AccountResult<CreatedAccount>> CreateSponsorAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            if (employeeId == Guid.Empty) return Fail<CreatedAccount>(AccountError.Validation, "Selecciona un empleado para crear la cuenta.");
            var employee = await employees.GetByIdAsync(employeeId, cancellationToken);
            if (employee is null) return Fail<CreatedAccount>(AccountError.NotFound, "No encontramos al empleado.");
            return await Create(employee.PayrollNumber, employee.Id, AccountRoles.Sponsor, cancellationToken);
        });

    public Task<AccountResult<CreatedAccount>> CreateAdministrativeAsync(string userName, string role, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            if (!AccountRoles.IsAdministrative(role) || string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > 256)
                return Fail<CreatedAccount>(AccountError.Validation, "Indica un usuario y un rol válidos.");
            return await Create(userName.Trim(), null, role, cancellationToken);
        });

    private Task<AccountResult<CreatedAccount>> Create(string name, Guid? employeeId, string role, CancellationToken token) =>
        transactions.RunAsync(async () =>
        {
            // El índice único de EmployeeId también protege frente a carreras y cuentas con otro nombre.
            if (await users.FindByNameAsync(name) is not null)
                return Fail<CreatedAccount>(AccountError.Duplicate, employeeId.HasValue
                    ? "Este empleado ya tiene una cuenta registrada." : "Este nombre de usuario ya está registrado.");
            if (!await roles.RoleExistsAsync(role))
            {
                var roleResult = await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
                if (!roleResult.Succeeded) return IdentityFailure<CreatedAccount>(roleResult);
            }
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = name, EmployeeId = employeeId,
                MustChangePassword = true, LockoutEnabled = true
            };
            var password = passwords.Generate();
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) return IdentityFailure<CreatedAccount>(created);
            var assigned = await users.AddToRoleAsync(user, role);
            if (!assigned.Succeeded) return IdentityFailure<CreatedAccount>(assigned);
            return new AccountResult<CreatedAccount>(new CreatedAccount(await Info(user), password));
        }, token);

    public Task<AccountResult<AccountInfo>> GetAccountAsync(Guid id, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = await users.FindByIdAsync(id.ToString());
            return user is null ? Fail<AccountInfo>(AccountError.NotFound, "No encontramos la cuenta.")
                : new AccountResult<AccountInfo>(await Info(user));
        });

    public Task<AccountResult<AccountInfo>> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password)) return Denied<AccountInfo>();
            var user = await users.FindByNameAsync(userName.Trim());
            if (user is null || !await CheckCredentials(user, password)) return Denied<AccountInfo>();
            return new AccountResult<AccountInfo>(await Info(user), Message: user.MustChangePassword
                ? "Debes cambiar tu contraseña antes de continuar." : null);
        });

    private async Task<bool> CheckCredentials(ApplicationUser user, string password)
    {
        if (await users.IsLockedOutAsync(user)) return false;
        if (!await users.CheckPasswordAsync(user, password))
        {
            // Identity aplica el umbral, incrementa el contador y establece el bloqueo.
            await users.AccessFailedAsync(user);
            return false;
        }
        var reset = await users.ResetAccessFailedCountAsync(user);
        return reset.Succeeded && !await users.IsLockedOutAsync(user);
    }

    public Task<AccountResult<AccountInfo>> ChangePasswordAsync(Guid id, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default) => Guard(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || string.IsNullOrEmpty(currentPassword) || !await CheckCredentials(user, currentPassword))
            return Denied<AccountInfo>();
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 12)
            return Fail<AccountInfo>(AccountError.Validation, "Usa una contraseña o frase de al menos 12 caracteres.");
        return await transactions.RunAsync(async () =>
        {
            // ChangePasswordAsync comprueba la actual, valida la nueva y renueva el security stamp.
            var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!changed.Succeeded) return IdentityFailure<AccountInfo>(changed);
            user.MustChangePassword = false;
            var saved = await users.UpdateAsync(user);
            if (!saved.Succeeded) return IdentityFailure<AccountInfo>(saved);
            return new AccountResult<AccountInfo>(await Info(user), Message: "Tu contraseña se actualizó correctamente.");
        }, cancellationToken);
    });

    private async Task<AccountInfo> Info(ApplicationUser user) => new(user.Id, user.UserName!, user.EmployeeId,
        user.MustChangePassword, (await users.GetRolesAsync(user)).Order().ToArray());

    private static AccountResult<T> IdentityFailure<T>(IdentityResult result)
    {
        var codes = result.Errors.Select(e => e.Code).ToArray();
        if (codes.Any(c => c.StartsWith("Duplicate", StringComparison.Ordinal)))
            return Fail<T>(AccountError.Duplicate, "La cuenta o el rol ya están registrados.");
        if (codes.Contains("ConcurrencyFailure"))
            return Fail<T>(AccountError.Concurrency, "La cuenta cambió durante la operación. Inténtalo nuevamente.");
        if (codes.Any(c => c.StartsWith("Password", StringComparison.Ordinal)))
            return Fail<T>(AccountError.Validation, "La contraseña no cumple los requisitos. Usa una frase de al menos 12 caracteres.");
        return Fail<T>(AccountError.Validation, "No pudimos completar la operación. Revisa los datos de la cuenta.");
    }

    private static async Task<AccountResult<T>> Guard<T>(Func<Task<AccountResult<T>>> operation)
    {
        try { return await operation(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { return Fail<T>(AccountError.Duplicate, "Este empleado o nombre de usuario ya tiene una cuenta registrada."); }
        catch (DbUpdateConcurrencyException)
        { return Fail<T>(AccountError.Concurrency, "La cuenta cambió durante la operación. Inténtalo nuevamente."); }
        catch (DbUpdateException) { return StorageFailure<T>(); }
        catch (SqlException) { return StorageFailure<T>(); }
        catch (EmployeePersistenceException) { return StorageFailure<T>(); }
    }
    private static AccountResult<T> StorageFailure<T>() => Fail<T>(AccountError.Persistence,
        "No pudimos completar la operación. Inténtalo nuevamente.");
}
