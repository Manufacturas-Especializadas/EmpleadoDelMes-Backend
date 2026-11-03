
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using EmpleadoDelMes.Application.Employees;

namespace EmpleadoDelMes.Infrastructure.Employees;

public sealed class ClosedXmlEmployeeReader : IEmployeeExcelReader
{
    private const int MaximumRows = 1000;
    private const int MaximumColumns = 30;

    public IReadOnlyList<ExcelEmployeeRow> Read(Stream stream)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);

            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new EmployeeExcelFormatException(
                    "El archivo no contiene hojas de Excel.");

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn =
                sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            if (lastRow < 2)
                throw new EmployeeExcelFormatException(
                    "El Excel debe contener encabezados y empleados.");

            if (lastRow - 1 > MaximumRows ||
                lastColumn > MaximumColumns)
                throw new EmployeeExcelFormatException(
                    "El archivo supera los límites permitidos.");

            int payrollColumn = 0;
            int nameColumn = 0;

            for (int column = 1; column <= lastColumn; column++)
            {
                var cell = sheet.Cell(1, column);

                if (!string.IsNullOrEmpty(cell.FormulaA1))
                    throw new EmployeeExcelFormatException(
                        "Los encabezados no pueden contener fórmulas.");

                var header = NormalizeHeader(cell.GetString());

                if (header is "nomina" or "numerodenomina"
                    or "numeronomina" or "payrollnumber")
                {
                    if (payrollColumn != 0)
                        throw new EmployeeExcelFormatException(
                            "El Excel contiene columnas de nómina repetidas.");

                    payrollColumn = column;
                }

                if (header is "nombre" or "nombrecompleto"
                    or "fullname")
                {
                    if (nameColumn != 0)
                        throw new EmployeeExcelFormatException(
                            "El Excel contiene columnas de nombre repetidas.");

                    nameColumn = column;
                }
            }

            if (payrollColumn == 0 || nameColumn == 0)
                throw new EmployeeExcelFormatException(
                    "El Excel requiere las columnas Nómina y Nombre completo.");

            var rows = new List<ExcelEmployeeRow>();

            for (int number = 2; number <= lastRow; number++)
            {
                var payrollCell = sheet.Cell(number, payrollColumn);
                var nameCell = sheet.Cell(number, nameColumn);

                var hasFormula = sheet.Row(number).Cells(1, lastColumn)
                    .Any(cell => !string.IsNullOrEmpty(cell.FormulaA1));

                // No evaluamos fórmulas del archivo.
                var payroll = hasFormula
                    ? ""
                    : payrollCell.GetFormattedString(
                        CultureInfo.InvariantCulture).Trim();

                var name = hasFormula
                    ? ""
                    : nameCell.GetString().Trim();

                // Ignorar filas completamente vacías.
                if (!hasFormula &&
                    string.IsNullOrWhiteSpace(payroll) &&
                    string.IsNullOrWhiteSpace(name))
                    continue;

                rows.Add(new ExcelEmployeeRow(
                    number,
                    payroll,
                    name,
                    hasFormula));
            }

            if (rows.Count == 0)
                throw new EmployeeExcelFormatException(
                    "No encontramos empleados en el archivo.");

            return rows;
        }
        catch (Exception ex) when (
            ex is InvalidDataException or
            ArgumentException or
            FormatException or
            System.Xml.XmlException)
        {
            throw new EmployeeExcelFormatException(
                "No pudimos leer el archivo. Verifica que sea un Excel .xlsx válido.");
        }
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.ToString();
    }
}
