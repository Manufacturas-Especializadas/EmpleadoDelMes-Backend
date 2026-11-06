namespace EmpleadoDelMes.Application.Assignments;

public enum AssignmentError { Validation, NotFound, Conflict, Persistence }
public sealed record AssignmentResult<T>(T? Value, AssignmentError? Error = null, string? Message = null)
{ public bool IsSuccess => Error is null; }
public sealed class AssignmentPersistenceException(AssignmentError error) : Exception("Assignment persistence operation failed.")
{ public AssignmentError Error { get; } = error; }
public enum AssignmentSearchKind { Sponsors, Collaborators, Available, Pending }
public enum AssignmentChange { Assign, Reassign, Withdraw }
public sealed record AssignmentEmployee(Guid Id, string PayrollNumber, string FullName);
public sealed record AssignmentPage(IReadOnlyList<AssignmentEmployee> Items, int Total, int PageNumber, int PageSize);
public sealed record AssignmentDto(Guid Id, Guid SponsorEmployeeId, Guid EmployeeId, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string Version);
public sealed record AssignmentSearch(AssignmentSearchKind Kind, Guid? SponsorId, string? Term, int PageNumber, int PageSize);
public interface IAssignmentRepository
{
    Task<AssignmentResult<AssignmentPage>> SearchAsync(AssignmentSearch search, CancellationToken token);
    Task<AssignmentResult<AssignmentDto>> CurrentAsync(Guid employeeId, CancellationToken token);
    Task<AssignmentResult<AssignmentDto>> ChangeAsync(Guid employeeId, Guid? sponsorId, AssignmentChange change, byte[]? version, CancellationToken token);
}
public sealed class AssignmentService(IAssignmentRepository repository)
{
    public Task<AssignmentResult<AssignmentPage>> SearchAsync(AssignmentSearchKind kind, Guid? sponsorId, string? term,
        int pageNumber, int pageSize, CancellationToken token)
    {
        if (!Enum.IsDefined(kind) || sponsorId == Guid.Empty ||
            (kind == AssignmentSearchKind.Collaborators && sponsorId is null) || pageNumber < 1 || pageSize < 1 || pageSize > 100 ||
            ((long)pageNumber - 1) * pageSize > int.MaxValue || term?.Length > 100)
            return Task.FromResult(new AssignmentResult<AssignmentPage>(null, AssignmentError.Validation,
                "Indica un patrocinador válido, una página positiva, tamaño entre 1 y 100 y búsqueda de hasta 100 caracteres."));
        return Execute(() => repository.SearchAsync(new(kind, sponsorId, term?.Trim(), pageNumber, pageSize), token));
    }
    public Task<AssignmentResult<AssignmentDto>> CurrentAsync(Guid employeeId, CancellationToken token) => employeeId == Guid.Empty
        ? Task.FromResult(new AssignmentResult<AssignmentDto>(null, AssignmentError.Validation, "Selecciona un empleado válido."))
        : Execute(() => repository.CurrentAsync(employeeId, token));

    public Task<AssignmentResult<AssignmentDto>> ChangeAsync(Guid employeeId, Guid? sponsorId, AssignmentChange change, string? version, CancellationToken token)
    {
        if (employeeId == Guid.Empty || !Enum.IsDefined(change) ||
            (change != AssignmentChange.Withdraw && (sponsorId is null || sponsorId == Guid.Empty || sponsorId == employeeId)))
            return Task.FromResult(new AssignmentResult<AssignmentDto>(null, AssignmentError.Validation, "Selecciona empleados válidos y distintos para la asignación."));
        byte[]? bytes = null;
        if (change != AssignmentChange.Assign)
        {
            bytes = new byte[8];
            if (version is null || !Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
                return Task.FromResult(new AssignmentResult<AssignmentDto>(null, AssignmentError.Validation, "Incluye la versión actual de la asignación."));
        }
        return Execute(() => repository.ChangeAsync(employeeId, sponsorId, change, bytes, token));
    }
    private static async Task<AssignmentResult<T>> Execute<T>(Func<Task<AssignmentResult<T>>> action)
    {
        try { return await action(); }
        catch (AssignmentPersistenceException ex)
        { return new(default, ex.Error, ex.Error == AssignmentError.Conflict
            ? "La asignación cambió durante la operación. Vuelve a consultarla."
            : "No pudimos completar la operación de asignaciones. Inténtalo nuevamente."); }
    }
}
