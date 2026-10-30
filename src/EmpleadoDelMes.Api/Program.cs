using EmpleadoDelMes.Api.Authentication;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SessionValidation>();
builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
    .AddBearerToken(IdentityConstants.BearerScheme, options =>
    {
        options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
        options.RefreshTokenExpiration = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    foreach (var role in new[] { AccountRoles.Administrator, AccountRoles.Sponsor,
        AccountRoles.HumanResources, AccountRoles.Safety, AccountRoles.Quality })
        options.AddPolicy(role, policy => policy.RequireAuthenticatedUser().RequireRole(role));
});

// 1. Controllers
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = _ =>
    new BadRequestObjectResult(new { message = "La solicitud no es válida. Revisa los datos enviados." }));

// 2. OpenAPI

builder.Services.AddOpenApi(options =>
{
    // Definir el esquema Bearer en Swagger.
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();

        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "Opaque",
                Description =
                    "Introduce únicamente el accessToken obtenido al iniciar sesión."
            };

        return Task.CompletedTask;
    });

    // Aplicar Bearer únicamente a los endpoints protegidos.
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var allowAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();

        if (requiresAuthorization &&
            !allowAnonymous &&
            context.Document is not null)
        {
            operation.Security ??= [];

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    "Bearer",
                    context.Document)] = []
            });
        }

        return Task.CompletedTask;
    });
});


// 3. Manejo centralizado de errores
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Title = "No pudimos completar la solicitud.";
    context.ProblemDetails.Detail = "Inténtalo nuevamente.";
});

// Health Checks
builder.Services.AddHealthChecks();

// 4. Configuración de CORS
const string corsPolicy = "FrontendCors";

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Must-Change-Password");
    });
});

var app = builder.Build();

// 5. Manejo centralizado de excepciones
app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    await response.WriteAsJsonAsync(new { message = response.StatusCode switch
    {
        401 => "Tu sesión ha vencido. Inicia sesión nuevamente.",
        403 => "No tienes permiso para realizar esta acción.",
        400 => "La solicitud no es válida. Revisa los datos enviados.",
        _ => "No pudimos completar la solicitud."
    }});
});

// 6. Documentación solo en desarrollo
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "EmpleadoDelMes API v1"
        );
    });
}

// 7. Pipeline HTTP
app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(corsPolicy);
app.UseAuthentication();
app.UseMiddleware<CurrentSessionMiddleware>();
app.UseAuthorization();

// 8. Controllers
app.MapControllers();

app.MapHealthChecks("/health").AllowAnonymous();

// 9. Logging
app.Logger.LogInformation(
    "Iniciando EmpleadoDelMes API en ambiente {Environment}",
    app.Environment.EnvironmentName
);

await app.Services.GetRequiredService<EmpleadoDelMes.Infrastructure.Identity.AdminBootstrapStartup>()
    .StartAsync(app.Lifetime.ApplicationStopping);

app.Run();

public partial class Program;
