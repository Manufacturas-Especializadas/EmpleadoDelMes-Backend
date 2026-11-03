using System.Globalization;
using System.Text;
using EmpleadoDelMes.Application.Employees;

namespace EmpleadoDelMes.Application.Accounts;

public enum SponsorImportStatus { Ready, ExistingSponsor, InactiveSponsor, EmployeeNotFound, DuplicatePayroll, NameMismatch, AccountConflict, InvalidData }
public sealed record SponsorImportRow(int RowNumber, string PayrollNumber, string ExcelName,
    string? CatalogName, SponsorImportStatus Status, string? Message);
public sealed record SponsorImportPreview(int TotalRows, int NewAccounts, int SkippedAccounts, int ErrorRows,
    IReadOnlyList<SponsorImportRow> Rows);
public sealed record SponsorImportConfirmation(int TotalRows, int CreatedAccounts, int SkippedAccounts);
public sealed record SponsorImportEmployee(Guid Id, string PayrollNumber, string FullName);
public sealed record SponsorImportAccount(Guid? EmployeeId, string UserName, bool IsActive, IReadOnlyList<string> Roles);
public sealed record SponsorImportSnapshot(IReadOnlyList<SponsorImportEmployee> Employees,
    IReadOnlyList<SponsorImportAccount> Accounts, IReadOnlySet<string> InvalidUserNames);
public interface ISponsorImportDirectory
{
    Task<SponsorImportSnapshot> ReadAsync(IReadOnlyList<string> payrolls, CancellationToken cancellationToken);
}
public interface ISponsorImportStore
{
    Task<AccountResult<SponsorImportPreview>> PreviewAsync(IReadOnlyList<ExcelEmployeeRow> rows, CancellationToken cancellationToken);
    Task<AccountResult<SponsorImportConfirmation>> ConfirmAsync(IReadOnlyList<ExcelEmployeeRow> rows, CancellationToken cancellationToken);
}

public sealed class SponsorImportService(IEmployeeExcelReader reader, ISponsorImportStore store)
{
    public Task<AccountResult<SponsorImportPreview>> PreviewAsync(Stream stream, CancellationToken token = default) =>
        store.PreviewAsync(Read(stream), token);

    public Task<AccountResult<SponsorImportConfirmation>> ConfirmAsync(Stream stream, bool confirmed, CancellationToken token = default)
    {
        if (!confirmed) return Task.FromResult(new AccountResult<SponsorImportConfirmation>(null, AccountError.Validation,
            "Confirma expresamente que deseas crear las cuentas de patrocinadores."));
        return store.ConfirmAsync(Read(stream), token);
    }

    private IReadOnlyList<ExcelEmployeeRow> Read(Stream stream)
    {
        var rows = reader.Read(stream);
        if (rows.Count is < 1 or > 1000) throw new EmployeeExcelFormatException("El archivo debe contener entre 1 y 1,000 registros.");
        return rows.Select(r => r with { PayrollNumber = r.PayrollNumber.Trim(), FullName = r.FullName.Trim() }).ToArray();
    }

    public static SponsorImportPreview Validate(IReadOnlyList<ExcelEmployeeRow> rows, SponsorImportSnapshot snapshot)
    {
        var duplicates = rows.GroupBy(r => r.PayrollNumber, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = rows.Select(row =>
        {
            var employee = snapshot.Employees.SingleOrDefault(e => string.Equals(e.PayrollNumber, row.PayrollNumber, StringComparison.OrdinalIgnoreCase));
            SponsorImportRow Result(SponsorImportStatus status, string? message = null) =>
                new(row.RowNumber, row.PayrollNumber, row.FullName, employee?.FullName, status, message);
            if (row.HasFormula || string.IsNullOrWhiteSpace(row.PayrollNumber) || row.PayrollNumber.Length > 32 ||
                string.IsNullOrWhiteSpace(row.FullName) || row.FullName.Length > 200 || snapshot.InvalidUserNames.Contains(row.PayrollNumber))
                return Result(SponsorImportStatus.InvalidData, "Revisa la nómina y el nombre. No se permiten fórmulas ni datos vacíos o fuera de los límites.");
            if (duplicates.Contains(row.PayrollNumber)) return Result(SponsorImportStatus.DuplicatePayroll, "La nómina está duplicada en el Excel.");
            if (employee is null) return Result(SponsorImportStatus.EmployeeNotFound, "No encontramos al empleado en el catálogo.");
            if (NormalizeName(employee.FullName) != NormalizeName(row.FullName))
                return Result(SponsorImportStatus.NameMismatch, "El nombre no corresponde al empleado del catálogo. Corrige el archivo.");
            var accounts = snapshot.Accounts.Where(a => a.EmployeeId == employee.Id ||
                string.Equals(a.UserName, row.PayrollNumber, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (accounts.Length == 0) return Result(SponsorImportStatus.Ready);
            if (accounts.Length != 1 || accounts[0].EmployeeId != employee.Id ||
                !string.Equals(accounts[0].UserName, employee.PayrollNumber, StringComparison.OrdinalIgnoreCase) ||
                accounts[0].Roles.Count != 1 || accounts[0].Roles[0] != AccountRoles.Sponsor)
                return Result(SponsorImportStatus.AccountConflict, "La cuenta tiene otro rol, otro vínculo o un nombre de usuario en conflicto.");
            return accounts[0].IsActive ? Result(SponsorImportStatus.ExistingSponsor, "La cuenta Sponsor ya existe y será omitida.")
                : Result(SponsorImportStatus.InactiveSponsor, "La cuenta Sponsor está desactivada. Será omitida y no se reactivará.");
        }).ToArray();
        var ready = results.Count(r => r.Status == SponsorImportStatus.Ready);
        var skipped = results.Count(r => r.Status is SponsorImportStatus.ExistingSponsor or SponsorImportStatus.InactiveSponsor);
        return new(results.Length, ready, skipped, results.Length - ready - skipped, results);
    }

    private static string NormalizeName(string value)
    {
        var letters = new string(value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return string.Join(' ', letters.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}
