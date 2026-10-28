var builder = WebApplication.CreateBuilder(args);

// 1. Controllers
builder.Services.AddControllers();

// 2. OpenAPI
builder.Services.AddOpenApi();

// 3. Manejo centralizado de errores
builder.Services.AddProblemDetails();

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
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 5. Manejo centralizado de excepciones
app.UseExceptionHandler();
app.UseStatusCodePages();

// 6. Documentación solo en desarrollo
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

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

// 8. Controllers
app.MapControllers();

app.MapHealthChecks("/health");

// 9. Logging
app.Logger.LogInformation(
    "Iniciando EmpleadoDelMes API en ambiente {Environment}",
    app.Environment.EnvironmentName
);

app.Run();
