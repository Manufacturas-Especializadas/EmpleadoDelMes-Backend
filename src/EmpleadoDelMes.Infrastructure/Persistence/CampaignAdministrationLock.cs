using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EmpleadoDelMes.Infrastructure.Persistence;

internal static class CampaignAdministrationLock
{
    // Shared by administrative transitions and nomination insertion. Caller owns the transaction.
    internal static async Task<bool> AcquireAsync(ApplicationDbContext context, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'EmpleadoDelMes.CampaignAdministration.v1',
                @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;
            SELECT @result;
            """;
        return (int)(await command.ExecuteScalarAsync(token))! >= 0;
    }
}
