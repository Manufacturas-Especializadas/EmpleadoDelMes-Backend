using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EmpleadoDelMes.Infrastructure.Identity;

public sealed class AdminBootstrapException(string message) : Exception(message);

// Resolve Identity/persistence only after explicit opt-in. Disabled startup never touches SQL.
public sealed class AdminBootstrapStartup(IConfiguration configuration, IServiceScopeFactory scopes,
    ILogger<AdminBootstrapStartup> logger)
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var setting = configuration["AdminBootstrap:Enabled"];
        if (setting is null) return;
        if (!bool.TryParse(setting, out var enabled))
            throw new AdminBootstrapException("La configuración de activación del administrador inicial no es válida.");
        if (!enabled) return;

        var name = configuration["AdminBootstrap:UserName"];
        var password = configuration["AdminBootstrap:Password"];
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
            throw new AdminBootstrapException("Configura el usuario y la contraseña del administrador inicial antes de iniciar la aplicación.");
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var created = await scope.ServiceProvider.GetRequiredService<AdminBootstrapInitializer>()
                .InitializeAsync(name, password, cancellationToken);
            logger.LogInformation(created
                ? "Inicialización administrativa completada. Retira las credenciales de la configuración."
                : "Inicialización administrativa omitida: ya fue completada o existe un administrador.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AdminBootstrapException) { throw; }
        catch (Exception)
        {
            // Do not attach provider/validator exceptions: they may contain configuration or account data.
            throw new AdminBootstrapException("No se pudo completar la inicialización administrativa. Revisa la configuración y la disponibilidad del servicio.");
        }
    }

}
