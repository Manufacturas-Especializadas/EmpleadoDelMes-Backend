
using System.Data;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Employees;

public sealed class EmployeeImportStore(
    ApplicationDbContext context) : IEmployeeImportStore
{
    public async Task<IReadOnlySet<string>> FindExistingAsync(
        IReadOnlyList<string> payrollNumbers,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            // Consultas por lotes para evitar listas enormes.
            foreach (var batch in payrollNumbers.Distinct().Chunk(500))
            {
                var found = await context.Employees
                    .AsNoTracking()
                    .Where(employee =>
                        batch.Contains(employee.PayrollNumber))
                    .Select(employee => employee.PayrollNumber)
                    .ToArrayAsync(cancellationToken);

                existing.UnionWith(found);
            }

            return existing;
        }
        catch (SqlException ex)
        {
            throw new EmployeePersistenceException(
                EmployeeError.Persistence, ex);
        }
    }

    public async Task<EmployeeImportSaveResult> InsertMissingAsync(
        IReadOnlyList<ExcelEmployeeRow> rows,
        CancellationToken cancellationToken)
    {
        try
        {
            // Garantiza que las altas se confirmen juntas.
            await using var transaction =
                await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            // Comprobamos nuevamente porque la BD puede haber
            // cambiado después de mostrar la vista previa.
            var payrolls = rows
                .Select(row => row.PayrollNumber)
                .ToArray();

            var existing = await FindExistingAsync(
                payrolls,
                cancellationToken);

            var newRows = rows
                .Where(row => !existing.Contains(row.PayrollNumber))
                .ToArray();

            var employees = newRows
                .Select(row => new Employee(
                    Guid.NewGuid(),
                    row.PayrollNumber,
                    row.FullName))
                .ToArray();

            context.Employees.AddRange(employees);

            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new EmployeeImportSaveResult(
                employees.Length,
                rows.Count - employees.Length);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException
            { Number: 2601 or 2627 or 1205 })
        {
            throw new EmployeePersistenceException(
                EmployeeError.Concurrency, ex);
        }
        catch (SqlException ex)
            when (ex.Number is 2601 or 2627 or 1205)
        {
            throw new EmployeePersistenceException(
                EmployeeError.Concurrency, ex);
        }
        catch (DbUpdateException ex)
        {
            throw new EmployeePersistenceException(
                EmployeeError.Persistence, ex);
        }
        catch (SqlException ex)
        {
            throw new EmployeePersistenceException(
                EmployeeError.Persistence, ex);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }
}
