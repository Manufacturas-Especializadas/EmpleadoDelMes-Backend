using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;

namespace EmpleadoDelMes.Infrastructure.Identity;

public sealed class SponsorImportStore(ISponsorImportDirectory directory, IdentityAccountService accounts,
    IAccountTransactionRunner transactions) : ISponsorImportStore
{
    public Task<AccountResult<SponsorImportPreview>> PreviewAsync(IReadOnlyList<ExcelEmployeeRow> rows, CancellationToken token) =>
        IdentityAccountService.Guard(async () => new AccountResult<SponsorImportPreview>(SponsorImportService.Validate(rows,
            await directory.ReadAsync(rows.Select(r => r.PayrollNumber).ToArray(), token))));

    public Task<AccountResult<SponsorImportConfirmation>> ConfirmAsync(IReadOnlyList<ExcelEmployeeRow> rows, CancellationToken token) =>
        IdentityAccountService.Guard(() => transactions.RunAsync(async () =>
        {
            var snapshot = await directory.ReadAsync(rows.Select(r => r.PayrollNumber).ToArray(), token);
            var preview = SponsorImportService.Validate(rows, snapshot);
            if (preview.ErrorRows > 0) return new AccountResult<SponsorImportConfirmation>(null, AccountError.Validation,
                "El archivo contiene errores o conflictos. Consulta la vista previa y corrígelos antes de confirmar.");
            foreach (var row in preview.Rows.Where(r => r.Status == SponsorImportStatus.Ready))
            {
                token.ThrowIfCancellationRequested();
                var employee = snapshot.Employees.Single(e => string.Equals(e.PayrollNumber, row.PayrollNumber, StringComparison.OrdinalIgnoreCase));
                var result = await accounts.CreateSponsorInTransactionAsync(employee);
                if (!result.IsSuccess) return new AccountResult<SponsorImportConfirmation>(null, result.Error, result.Message);
            }
            return new AccountResult<SponsorImportConfirmation>(new(rows.Count, preview.NewAccounts, preview.SkippedAccounts));
        }, token));
}
