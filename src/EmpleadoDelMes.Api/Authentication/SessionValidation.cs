using System.Security.Claims;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace EmpleadoDelMes.Api.Authentication;

// No cache: password changes and role changes take effect on the next request.
public sealed class SessionValidation(UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principals)
{
    public async Task<ApplicationUser?> ValidateAsync(ClaimsPrincipal principal)
    {
        var id = users.GetUserId(principal);
        if (!Guid.TryParse(id, out _)) return null;
        var user = await users.FindByIdAsync(id);
        var stamp = principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType);
        if (user is null || string.IsNullOrEmpty(stamp) ||
            stamp != await users.GetSecurityStampAsync(user) || await users.IsLockedOutAsync(user)) return null;
        return user;
    }

    public Task<ClaimsPrincipal> CreatePrincipalAsync(ApplicationUser user) => principals.CreateAsync(user);
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowTemporaryPasswordAttribute : Attribute;
