using System.Security.Claims;
using EmpleadoDelMes.Application.Accounts;
using Microsoft.AspNetCore.Identity;

namespace EmpleadoDelMes.Infrastructure.Identity;

public sealed class AdminBootstrapInitializer(UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles, IAdminBootstrapTransaction transaction)
{
    // Lifecycle metadata, not an authorization permission. Retained when an administrator user is deleted.
    public const string CompletionClaimType = "EmpleadoDelMes:AdminBootstrap";
    private const string CompletionClaimValue = "Completed:v1";

    public async Task<bool> InitializeAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var allowed = users.Options.User.AllowedUserNameCharacters;
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 256 || userName != userName.Trim() ||
            (!string.IsNullOrEmpty(allowed) && userName.Any(c => !allowed.Contains(c))) || string.IsNullOrWhiteSpace(password))
            throw new AdminBootstrapException("El usuario o la contraseña de inicialización no son válidos.");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = userName, EmployeeId = null,
            MustChangePassword = true, LockoutEnabled = true
        };
        foreach (var validator in users.PasswordValidators)
            RequireSuccess(await validator.ValidateAsync(users, user, password),
                "La contraseña inicial no cumple la política de seguridad.");

        return await transaction.RunAsync(async () =>
        {
            var adminRole = await roles.FindByNameAsync(AccountRoles.Administrator);
            if (adminRole is not null && (await roles.GetClaimsAsync(adminRole)).Any(c =>
                c.Type == CompletionClaimType && c.Value == CompletionClaimValue)) return false;

            if (adminRole is not null && (await users.GetUsersInRoleAsync(AccountRoles.Administrator)).Count != 0)
            {
                await MarkCompletedAsync(adminRole);
                return false;
            }

            // Never elevate an existing account or reset its password.
            if (await users.FindByNameAsync(userName) is not null)
                throw new AdminBootstrapException("El usuario configurado ya está registrado. No se modificó su cuenta.");
            foreach (var name in new[] { AccountRoles.Administrator, AccountRoles.Sponsor,
                AccountRoles.HumanResources, AccountRoles.Safety, AccountRoles.Quality })
            {
                if (!await roles.RoleExistsAsync(name))
                    RequireSuccess(await roles.CreateAsync(new IdentityRole<Guid>(name) { Id = Guid.NewGuid() }),
                        "No se pudieron preparar los roles iniciales.");
            }
            RequireSuccess(await users.CreateAsync(user, password), "No se pudo crear la cuenta administrativa inicial.");
            RequireSuccess(await users.AddToRoleAsync(user, AccountRoles.Administrator), "No se pudo completar la cuenta administrativa inicial.");
            await MarkCompletedAsync((await roles.FindByNameAsync(AccountRoles.Administrator))!);
            return true;
        }, cancellationToken);
    }

    private async Task MarkCompletedAsync(IdentityRole<Guid> role) =>
        RequireSuccess(await roles.AddClaimAsync(role, new Claim(CompletionClaimType, CompletionClaimValue)),
            "No se pudo confirmar la inicialización administrativa.");

    private static void RequireSuccess(IdentityResult result, string message)
    {
        if (!result.Succeeded) throw new AdminBootstrapException(message);
    }
}
