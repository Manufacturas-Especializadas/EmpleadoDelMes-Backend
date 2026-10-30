using System.Linq.Expressions;
using EmpleadoDelMes.Application.Employees;
using EmpleadoDelMes.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class EmployeeRepository(ApplicationDbContext context) : IEmployeeRepository
{
    private sealed record EmployeeRow(Guid Id, string PayrollNumber, string FullName, byte[] Version);
    private static readonly Expression<Func<Employee, EmployeeRow>> Projection = employee =>
        new EmployeeRow(employee.Id, employee.PayrollNumber, employee.FullName, EF.Property<byte[]>(employee, "RowVersion"));
    private static EmployeeDto ToDto(EmployeeRow row) => new(row.Id, row.PayrollNumber, row.FullName, Convert.ToBase64String(row.Version));

    public Task<EmployeeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        ReadAsync(context.Employees.AsNoTracking().Where(e => e.Id == id), cancellationToken);
    public Task<EmployeeDto?> GetByPayrollNumberAsync(string payrollNumber, CancellationToken cancellationToken) =>
        ReadAsync(context.Employees.AsNoTracking().Where(e => e.PayrollNumber == payrollNumber), cancellationToken);

    private static async Task<EmployeeDto?> ReadAsync(IQueryable<Employee> query, CancellationToken cancellationToken)
    {
        try
        {
            var row = await query.Select(Projection).SingleOrDefaultAsync(cancellationToken);
            return row is null ? null : ToDto(row);
        }
        catch (SqlException ex) { throw new EmployeePersistenceException(EmployeeError.Persistence, ex); }
    }

    public async Task<EmployeePage> SearchAsync(string term, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            var query = context.Employees.AsNoTracking();
            if (term.Length > 0) query = query.Where(e => e.PayrollNumber.Contains(term) || e.FullName.Contains(term));
            var total = await query.CountAsync(cancellationToken);
            var rows = await query.OrderBy(e => e.PayrollNumber).ThenBy(e => e.Id)
                .Skip(checked((pageNumber - 1) * pageSize)).Take(pageSize).Select(Projection).ToListAsync(cancellationToken);
            return new(rows.Select(ToDto).ToArray(), total, pageNumber, pageSize);
        }
        catch (SqlException ex) { throw new EmployeePersistenceException(EmployeeError.Persistence, ex); }
    }

    public Task<EmployeeDto> CreateAsync(Employee employee, CancellationToken cancellationToken) =>
        SaveAsync(employee, null, cancellationToken);
    public Task<EmployeeDto> UpdateNameAsync(Employee employee, byte[] expectedVersion, CancellationToken cancellationToken) =>
        SaveAsync(employee, expectedVersion, cancellationToken);

    private async Task<EmployeeDto> SaveAsync(Employee employee, byte[]? expectedVersion, CancellationToken cancellationToken)
    {
        var entry = expectedVersion is null ? context.Employees.Add(employee) : context.Employees.Attach(employee);
        if (expectedVersion is not null)
        {
            entry.Property<byte[]>("RowVersion").OriginalValue = expectedVersion.ToArray();
            entry.Property(e => e.FullName).IsModified = true;
        }
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(employee.Id, employee.PayrollNumber, employee.FullName,
                Convert.ToBase64String(entry.Property<byte[]>("RowVersion").CurrentValue!));
        }
        catch (DbUpdateConcurrencyException ex) { throw new EmployeePersistenceException(EmployeeError.Concurrency, ex); }
        catch (DbUpdateException ex) when (expectedVersion is null && ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new EmployeePersistenceException(EmployeeError.Duplicate, ex); }
        catch (DbUpdateException ex) { throw new EmployeePersistenceException(EmployeeError.Persistence, ex); }
        catch (SqlException ex) { throw new EmployeePersistenceException(EmployeeError.Persistence, ex); }
        finally { entry.State = EntityState.Detached; }
    }
}
