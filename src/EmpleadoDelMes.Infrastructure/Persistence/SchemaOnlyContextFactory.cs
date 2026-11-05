using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EmpleadoDelMes.Infrastructure.Persistence;

// Explicit schema-only tooling: never loads secrets, starts the API, or connects to SQL Server.
public sealed class SchemaOnlyContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        if (!args.Contains("--schema-only", StringComparer.Ordinal))
            throw new InvalidOperationException("Usa -- --schema-only para generar o inspeccionar migraciones sin ejecutar el host.");
        return new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=SchemaOnly_NotAnApplicationDatabase;Integrated Security=True;Connect Timeout=1")
            .Options);
    }
}
