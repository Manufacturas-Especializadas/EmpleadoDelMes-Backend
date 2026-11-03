
namespace EmpleadoDelMes.Application.Employees;

public sealed record ExcelEmployeeRow(
    int RowNumber,
    string PayrollNumber,
    string FullName,
    bool HasFormula);

public sealed record EmployeeImportRow(
    int RowNumber,
    string PayrollNumber,
    string FullName,
    bool IsValid,
    IReadOnlyList<string> Errors,
    bool ExistsInDatabase = false);

public sealed record EmployeeImportPreview(
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    IReadOnlyList<EmployeeImportRow> Rows,
    int NewRows = 0,
    int ExistingRows = 0);

public sealed class EmployeeExcelFormatException(string message)
    : Exception(message);

public interface IEmployeeExcelReader
{
    IReadOnlyList<ExcelEmployeeRow> Read(Stream stream);
}


public sealed record EmployeeImportSaveResult(
    int Inserted,
    int SkippedExisting);

public sealed record EmployeeImportConfirmation(
    int TotalRows,
    int InsertedRows,
    int SkippedExistingRows);

public interface IEmployeeImportStore
{
    Task<IReadOnlySet<string>> FindExistingAsync(
        IReadOnlyList<string> payrollNumbers,
        CancellationToken cancellationToken);

    Task<EmployeeImportSaveResult> InsertMissingAsync(
        IReadOnlyList<ExcelEmployeeRow> rows,
        CancellationToken cancellationToken);
}


public sealed class EmployeeImportPreviewService(
    IEmployeeExcelReader reader,
    IEmployeeImportStore store)
{
    public EmployeeImportPreview Preview(Stream stream)
    {
        var sourceRows = reader.Read(stream);

        var payrolls = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var results = new List<EmployeeImportRow>();

        foreach (var row in sourceRows)
        {
            var payroll = row.PayrollNumber.Trim();
            var name = row.FullName.Trim();
            var errors = new List<string>();

            if (row.HasFormula)
                errors.Add("No se permiten fórmulas en los datos.");

            if (string.IsNullOrWhiteSpace(payroll))
                errors.Add("El número de nómina es obligatorio.");
            else if (payroll.Length > 32)
                errors.Add("La nómina no puede superar 32 caracteres.");

            if (string.IsNullOrWhiteSpace(name))
                errors.Add("El nombre completo es obligatorio.");
            else if (name.Length > 200)
                errors.Add("El nombre no puede superar 200 caracteres.");

            if (!string.IsNullOrWhiteSpace(payroll) &&
                !payrolls.Add(payroll))
            {
                errors.Add("La nómina está duplicada en el Excel.");
            }

            results.Add(new EmployeeImportRow(
                row.RowNumber,
                payroll,
                name,
                errors.Count == 0,
                errors));
        }

        return new EmployeeImportPreview(
            results.Count,
            results.Count(x => x.IsValid),
            results.Count(x => !x.IsValid),
            results);
    }


    public async Task<EmployeeImportPreview> PreviewAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var preview = Preview(stream);

        var payrolls = preview.Rows
            .Where(row => row.IsValid)
            .Select(row => row.PayrollNumber)
            .ToArray();

        if (payrolls.Length == 0)
            return preview;

        var existing = await store.FindExistingAsync(
            payrolls,
            cancellationToken);

        var rows = preview.Rows
            .Select(row => row with
            {
                ExistsInDatabase =
                    row.IsValid &&
                    existing.Contains(row.PayrollNumber)
            })
            .ToArray();

        var existingRows = rows.Count(
            row => row.ExistsInDatabase);

        return preview with
        {
            Rows = rows,
            ExistingRows = existingRows,
            NewRows = preview.ValidRows - existingRows
        };
    }

    public async Task<EmployeeImportConfirmation> ConfirmAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        // Volver a leer y validar el Excel.
        // No confiamos en una vista previa anterior.
        var preview = Preview(stream);

        if (preview.InvalidRows > 0)
        {
            throw new EmployeeExcelFormatException(
                "El archivo contiene filas con errores. " +
                "Corrígelas antes de confirmar la importación.");
        }

        var rows = preview.Rows
            .Select(row => new ExcelEmployeeRow(
                row.RowNumber,
                row.PayrollNumber,
                row.FullName,
                false))
            .ToArray();

        var result = await store.InsertMissingAsync(
            rows,
            cancellationToken);

        return new EmployeeImportConfirmation(
            preview.TotalRows,
            result.Inserted,
            result.SkippedExisting);
    }



}
