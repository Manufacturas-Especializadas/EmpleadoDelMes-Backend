namespace EmpleadoDelMes.Application.Accounts;

public static class AccountRoles
{
    public const string Administrator = "Administrator";
    public const string Sponsor = "Sponsor";
    public const string HumanResources = "HumanResources";
    public const string Safety = "Safety";
    public const string Quality = "Quality";
    public static bool IsAdministrative(string role) =>
        role is Administrator or HumanResources or Safety or Quality;
}
public enum AccountError { Validation, NotFound, Duplicate, InvalidCredentials, Concurrency, Persistence }
public sealed record AccountInfo(Guid Id, string UserName, Guid? EmployeeId, bool MustChangePassword, IReadOnlyList<string> Roles);
public sealed record AccountResult<T>(T? Value, AccountError? Error = null, string? Message = null)
{
    public bool IsSuccess => Error is null;
}
// Solo el resultado del aprovisionamiento contiene el secreto. Nunca se incluye en consultas.
public sealed class CreatedAccount(AccountInfo account, string temporaryPassword)
{
    public AccountInfo Account { get; } = account;
    public string TemporaryPassword { get; } = temporaryPassword;
    public override string ToString() => "CreatedAccount";
}
public interface IAccountService
{
    Task<AccountResult<CreatedAccount>> CreateSponsorAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<AccountResult<CreatedAccount>> CreateAdministrativeAsync(string userName, string role, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountInfo>> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountInfo>> GetAccountAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountInfo>> ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
