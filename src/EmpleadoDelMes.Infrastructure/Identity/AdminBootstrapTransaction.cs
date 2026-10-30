using System.Data;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EmpleadoDelMes.Infrastructure.Identity;

// Specific transaction boundary for the one-time bootstrap; all instances share the SQL lock.
public interface IAdminBootstrapTransaction
{
    Task<bool> RunAsync(Func<Task<bool>> operation, CancellationToken cancellationToken);
}

public sealed class AdminBootstrapTransaction(ApplicationDbContext context) : IAdminBootstrapTransaction
{
    public async Task<bool> RunAsync(Func<Task<bool>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = N'EmpleadoDelMes.AdminBootstrap.v1',
                    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;
                SELECT @result;
                """;
            var result = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (result < 0)
                throw new AdminBootstrapException("No se pudo coordinar la inicialización administrativa. Inténtalo nuevamente.");
            var created = await operation();
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        finally
        {
            // Transaction disposal rolls back failures and releases the application lock.
            context.ChangeTracker.Clear();
        }
    }
}
