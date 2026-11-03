
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

public sealed class EmployeeImportFileRequest
{
    public IFormFile? File { get; set; }
}

[ApiController]
[Route("api/employees/import")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class EmployeeImportsController(
    EmployeeImportPreviewService importService)
    : ControllerBase
{
    private const long MaximumFileSize = 5 * 1024 * 1024;

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Preview(
        [FromForm] EmployeeImportFileRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateFile(request.File);

        if (validation is not null)
            return validation;

        try
        {
            using var stream = request.File!.OpenReadStream();

            var result = await importService.PreviewAsync(
                stream,
                cancellationToken);

            return Ok(result);
        }
        catch (EmployeeExcelFormatException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (EmployeePersistenceException)
        {
            return StatusCode(503, new
            {
                message =
                    "No pudimos consultar los empleados. " +
                    "Inténtalo nuevamente."
            });
        }
    }

    [HttpPost("confirm")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Confirm(
        [FromForm] EmployeeImportFileRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateFile(request.File);

        if (validation is not null)
            return validation;

        try
        {
            using var stream = request.File!.OpenReadStream();

            var result = await importService.ConfirmAsync(
                stream,
                cancellationToken);

            return Ok(new
            {
                message = "La importación terminó correctamente.",
                result.TotalRows,
                result.InsertedRows,
                result.SkippedExistingRows
            });
        }
        catch (EmployeeExcelFormatException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (EmployeePersistenceException ex)
            when (ex.Error == EmployeeError.Concurrency)
        {
            return Conflict(new
            {
                message =
                    "La información cambió durante la importación. " +
                    "Vuelve a consultar la vista previa."
            });
        }
        catch (EmployeePersistenceException)
        {
            return StatusCode(503, new
            {
                message =
                    "No pudimos guardar los empleados. " +
                    "Inténtalo nuevamente."
            });
        }
    }

    private IActionResult? ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new
            {
                message = "Selecciona un archivo Excel."
            });
        }

        if (file.Length > MaximumFileSize)
        {
            return BadRequest(new
            {
                message = "El archivo no puede superar 5 MB."
            });
        }

        if (!string.Equals(
            Path.GetExtension(file.FileName),
            ".xlsx",
            StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Solo se permiten archivos .xlsx."
            });
        }

        return null;
    }
}
