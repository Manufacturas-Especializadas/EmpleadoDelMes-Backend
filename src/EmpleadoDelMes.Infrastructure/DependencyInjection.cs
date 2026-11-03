using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Infrastructure.Employees;
using EmpleadoDelMes.Infrastructure.Identity;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<CampaignService>();
        services.AddScoped<INominationRepository, NominationRepository>();
        services.AddScoped<NominationService>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<IdentityAccountService>();
        services.AddScoped<IAccountService>(provider => provider.GetRequiredService<IdentityAccountService>());
        services.AddScoped<ISponsorImportDirectory, SponsorImportDirectory>();
        services.AddScoped<ISponsorImportStore, SponsorImportStore>();
        services.AddScoped<SponsorImportService>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();
        services.AddScoped<IAccountTransactionRunner, AccountTransactionRunner>();
        services.AddSingleton<TemporaryPasswordGenerator>();
        services.AddScoped<AdminBootstrapInitializer>();
        services.AddScoped<IAdminBootstrapTransaction, AdminBootstrapTransaction>();
        services.AddSingleton<AdminBootstrapStartup>();
        services.AddScoped<IEmployeeExcelReader, ClosedXmlEmployeeReader>();
        services.AddScoped<EmployeeImportPreviewService>();
        services.AddScoped<IEmployeeImportStore, EmployeeImportStore>();
        return services;
    }
}
