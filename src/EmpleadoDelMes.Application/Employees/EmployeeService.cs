using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Application.Employees;

public sealed class EmployeeService(IEmployeeRepository repository)
{
    public const int MaximumPageSize = 100;

    public Task<EmployeeResult<EmployeeDto>> CreateEmployee(string? payrollNumber, string? fullName, CancellationToken cancellationToken = default)
    {
        var error = ValidatePayroll(payrollNumber) ?? ValidateName(fullName);
        if (error is not null) return Task.FromResult(Invalid<EmployeeDto>(error));
        var employee = new Employee(Guid.NewGuid(), payrollNumber!, fullName!);
        // El índice único, no una consulta previa, decide ante inserciones simultáneas.
        return Execute(() => repository.CreateAsync(employee, cancellationToken));
    }

    public Task<EmployeeResult<EmployeeDto>> GetEmployeeById(Guid id, CancellationToken cancellationToken = default)
        => id == Guid.Empty ? Task.FromResult(Invalid<EmployeeDto>("Selecciona un empleado válido."))
            : Find(() => repository.GetByIdAsync(id, cancellationToken), "No encontramos al empleado.");

    public Task<EmployeeResult<EmployeeDto>> GetEmployeeByPayrollNumber(string? payrollNumber, CancellationToken cancellationToken = default)
    {
        var error = ValidatePayroll(payrollNumber);
        return error is not null ? Task.FromResult(Invalid<EmployeeDto>(error))
            : Find(() => repository.GetByPayrollNumberAsync(payrollNumber!.Trim(), cancellationToken),
                "No encontramos un empleado con ese número de nómina.");
    }

    public Task<EmployeeResult<EmployeePage>> SearchEmployees(string? term, int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > MaximumPageSize
            || (long)(pageNumber - 1) * pageSize > int.MaxValue)
            return Task.FromResult(Invalid<EmployeePage>("Indica una página válida y un tamaño entre 1 y 100."));
        return Execute(() => repository.SearchAsync(term?.Trim() ?? "", pageNumber, pageSize, cancellationToken));
    }

    public async Task<EmployeeResult<EmployeeDto>> UpdateEmployeeName(Guid id, string? fullName, string? version,
        CancellationToken cancellationToken = default)
    {
        var error = ValidateName(fullName);
        if (error is not null) return Invalid<EmployeeDto>(error);
        var bytes = new byte[8];
        if (version is null || !Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
            return Invalid<EmployeeDto>("Vuelve a consultar al empleado antes de guardar los cambios.");
        var current = await GetEmployeeById(id, cancellationToken);
        if (!current.IsSuccess) return current;
        var employee = new Employee(current.Value!.Id, current.Value.PayrollNumber, current.Value.FullName);
        employee.CorrectName(fullName!);
        return await Execute(() => repository.UpdateNameAsync(employee, bytes, cancellationToken));
    }

    private static string? ValidatePayroll(string? value) => string.IsNullOrWhiteSpace(value)
        ? "El número de nómina es obligatorio." : value.Trim().Length > 32
        ? "El número de nómina no puede tener más de 32 caracteres." : null;
    private static string? ValidateName(string? value) => string.IsNullOrWhiteSpace(value)
        ? "Ingresa el nombre completo del empleado." : value.Trim().Length > 200
        ? "El nombre completo no puede tener más de 200 caracteres." : null;
    private static EmployeeResult<T> Invalid<T>(string message) => new(default, EmployeeError.Validation, message);

    private static async Task<EmployeeResult<EmployeeDto>> Find(Func<Task<EmployeeDto?>> query, string message)
    {
        var result = await Execute(query);
        return result.IsSuccess && result.Value is null
            ? new(null, EmployeeError.NotFound, message)
            : new(result.Value, result.Error, result.Message);
    }

    private static async Task<EmployeeResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return new(await action()); }
        catch (EmployeePersistenceException exception)
        {
            var message = exception.Error switch
            {
                EmployeeError.Duplicate => "Ya existe un empleado registrado con ese número de nómina.",
                EmployeeError.Concurrency => "Otro usuario modificó este empleado. Vuelve a consultarlo antes de guardar.",
                _ => "No pudimos completar la operación. Inténtalo nuevamente."
            };
            return new(default, exception.Error, message);
        }
    }
}
