using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Infrastructure.Identity;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EmpleadoDelMes.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            // Validar al resolver el contexto permite iniciar los endpoints técnicos sin SQL Server.
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Configure ConnectionStrings:DefaultConnection antes de utilizar la persistencia.");
            }

            options.UseSqlServer(connectionString);
        });

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Stores.MaxLengthForKeys = 128;
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<IAccountService, IdentityAccountService>();
        services.AddScoped<IAccountTransactionRunner, AccountTransactionRunner>();
        services.AddSingleton<TemporaryPasswordGenerator>();
        services.AddScoped<AdminBootstrapInitializer>();
        services.AddScoped<IAdminBootstrapTransaction, AdminBootstrapTransaction>();
        services.AddSingleton<AdminBootstrapStartup>();
        return services;
    }
}
