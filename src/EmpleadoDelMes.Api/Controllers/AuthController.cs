using EmpleadoDelMes.Api.Authentication;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IAccountService accounts, UserManager<ApplicationUser> users,
    SessionValidation sessions, IEmployeeRepository employees, IOptionsMonitor<BearerTokenOptions> bearerOptions,
    TimeProvider clock) : ControllerBase
{
    public sealed record LoginRequest(string? UserName, string? Password);
    public sealed record PasswordRequest(string? CurrentPassword, string? NewPassword);
    public sealed record RefreshRequest(string? RefreshToken);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
            return BadRequest(new { message = "Ingresa el usuario y la contraseña." });
        var before = await users.FindByNameAsync(request.UserName.Trim());
        var result = await accounts.ValidateCredentialsAsync(request.UserName, request.Password, cancellationToken);
        if (!result.IsSuccess) return Failure(result);
        var user = await users.FindByIdAsync(result.Value!.Id.ToString());
        if (user is null || before?.SecurityStamp != user.SecurityStamp || await users.IsLockedOutAsync(user))
            return InvalidSession();
        // Keep Identity's official token response unchanged; React also receives this exposed header.
        Response.Headers["X-Must-Change-Password"] = user.MustChangePassword ? "true" : "false";
        return SignIn(await sessions.CreatePrincipalAsync(user), IdentityConstants.BearerScheme);
    }

    [HttpGet("me")]
    [AllowTemporaryPassword]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await accounts.GetAccountAsync(Guid.Parse(users.GetUserId(User)!), cancellationToken);
        if (!result.IsSuccess) return Failure(result);
        var account = result.Value!;
        var employee = account.EmployeeId is { } id ? await employees.GetByIdAsync(id, cancellationToken) : null;
        return Ok(new { userId = account.Id, account.UserName, account.Roles,
            displayName = employee?.FullName, account.MustChangePassword });
    }

    [HttpPost("change-password")]
    [AllowTemporaryPassword]
    public async Task<IActionResult> ChangePassword(PasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { message = "Ingresa la contraseña actual y la nueva contraseña." });
        var result = await accounts.ChangePasswordAsync(Guid.Parse(users.GetUserId(User)!),
            request.CurrentPassword, request.NewPassword, cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Tu contraseña se actualizó correctamente. Inicia sesión nuevamente." })
            : Failure(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { message = "Se requiere el token de renovación." });
        var options = bearerOptions.Get(IdentityConstants.BearerScheme);
        var ticket = options.RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || clock.GetUtcNow() >= expires)
            return InvalidSession();
        var user = await sessions.ValidateAsync(ticket.Principal);
        if (user is null) return InvalidSession();
        if (user.MustChangePassword)
            return StatusCode(403, new { message = "Debes cambiar tu contraseña antes de continuar." });
        return SignIn(await sessions.CreatePrincipalAsync(user), IdentityConstants.BearerScheme);
    }

    private ObjectResult InvalidSession()
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return StatusCode(401, new { message = "Tu sesión ha vencido. Inicia sesión nuevamente." });
    }

    private ObjectResult Failure(AccountResult<AccountInfo> result)
    {
        var code = result.Error switch
        {
            AccountError.Validation => 400,
            AccountError.InvalidCredentials or AccountError.NotFound => 401,
            AccountError.Concurrency or AccountError.Duplicate => 409,
            _ => 503
        };
        if (code == 401) Response.Headers.WWWAuthenticate = "Bearer";
        return StatusCode(code, new { message = result.Message ?? "No pudimos completar la operación. Inténtalo nuevamente." });
    }
}
