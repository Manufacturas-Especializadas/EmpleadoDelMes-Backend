using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Infrastructure.Persistence;

namespace EmpleadoDelMes.Infrastructure.Identity;

// Contrato técnico limitado a las operaciones de cuentas que realizan varias escrituras Identity.
public interface IAccountTransactionRunner
{
    Task<AccountResult<T>> RunAsync<T>(Func<Task<AccountResult<T>>> operation, CancellationToken cancellationToken);
}
public sealed class AccountTransactionRunner(ApplicationDbContext context) : IAccountTransactionRunner
{
    public async Task<AccountResult<T>> RunAsync<T>(Func<Task<AccountResult<T>>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var result = await operation();
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
            else await transaction.RollbackAsync(cancellationToken);
            return result;
        }
        finally
        {
            // Tras rollback no reutilizar instancias con hashes, roles o flags no confirmados.
            if (!committed) context.ChangeTracker.Clear();
        }
    }
}
