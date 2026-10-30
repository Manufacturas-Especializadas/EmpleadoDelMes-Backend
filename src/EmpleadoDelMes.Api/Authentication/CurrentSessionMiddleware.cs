using Microsoft.AspNetCore.Authorization;

namespace EmpleadoDelMes.Api.Authentication;

public sealed class CurrentSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Anonymous technical endpoints and login/refresh do not depend on an access token.
        var endpoint = context.GetEndpoint();
        if (endpoint is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null &&
            context.User.Identity?.IsAuthenticated == true)
        {
            var sessions = context.RequestServices.GetRequiredService<SessionValidation>();
            var user = await sessions.ValidateAsync(context.User);
            if (user is null)
            {
                context.Response.StatusCode = 401;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await context.Response.WriteAsJsonAsync(new { message = "Tu sesión ha vencido. Inicia sesión nuevamente." });
                return;
            }
            context.User = await sessions.CreatePrincipalAsync(user);
            if (user.MustChangePassword && endpoint.Metadata.GetMetadata<AllowTemporaryPasswordAttribute>() is null)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { message = "Debes cambiar tu contraseña antes de continuar." });
                return;
            }
        }
        await next(context);
    }
}
