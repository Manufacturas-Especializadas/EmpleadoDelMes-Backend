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

        services.AddIdentityCore<ApplicationUser>(options => options.Stores.MaxLengthForKeys = 128)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        return services;
    }
}
