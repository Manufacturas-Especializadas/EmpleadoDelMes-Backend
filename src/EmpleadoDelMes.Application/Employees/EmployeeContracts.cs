using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Application.Employees;

public sealed record EmployeeDto(Guid Id, string PayrollNumber, string FullName, string Version);
public sealed record EmployeePage(IReadOnlyList<EmployeeDto> Items, int Total, int PageNumber, int PageSize);
public enum EmployeeError { Validation, NotFound, Duplicate, Concurrency, Persistence }
public sealed record EmployeeResult<T>(T? Value, EmployeeError? Error = null, string? Message = null)
{
    public bool IsSuccess => Error is null;
}
public sealed class EmployeePersistenceException(EmployeeError error, Exception? inner = null)
    : Exception("Employee persistence operation failed.", inner)
{
    public EmployeeError Error { get; } = error;
}
public interface IEmployeeRepository
{
    Task<EmployeeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<EmployeeDto?> GetByPayrollNumberAsync(string payrollNumber, CancellationToken cancellationToken);
    Task<EmployeePage> SearchAsync(string term, int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<EmployeeDto> CreateAsync(Employee employee, CancellationToken cancellationToken);
    Task<EmployeeDto> UpdateNameAsync(Employee employee, byte[] expectedVersion, CancellationToken cancellationToken);
}
