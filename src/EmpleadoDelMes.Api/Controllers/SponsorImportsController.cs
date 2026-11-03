using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

public sealed class SponsorImportFileRequest
{
    public IFormFile? File { get; set; }
    public bool Confirm { get; set; }
}

[ApiController]
[Route("api/users/import/sponsors")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Consumes("multipart/form-data")]
[RequestSizeLimit(6 * 1024 * 1024)]
public sealed class SponsorImportsController(SponsorImportService service) : ControllerBase
{
    [HttpPost("preview")]
    public Task<IActionResult> Preview([FromForm] SponsorImportFileRequest request, CancellationToken token) =>
        Execute(request, stream => service.PreviewAsync(stream, token));

    [HttpPost("confirm")]
    public Task<IActionResult> Confirm([FromForm] SponsorImportFileRequest request, CancellationToken token) =>
        Execute(request, stream => service.ConfirmAsync(stream, request.Confirm, token));

    private async Task<IActionResult> Execute<T>(SponsorImportFileRequest request, Func<Stream, Task<AccountResult<T>>> operation)
    {
        if (request.File is null || request.File.Length == 0)
            return BadRequest(new { message = "Selecciona un archivo Excel." });
        if (request.File.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "El archivo no puede superar 5 MB." });
        if (!string.Equals(Path.GetExtension(request.File.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Solo se permiten archivos .xlsx." });
        try
        {
            using var stream = request.File.OpenReadStream();
            var result = await operation(stream);
            return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error switch
            {
                AccountError.Validation => 400,
                AccountError.Concurrency or AccountError.Duplicate => 409,
                _ => 503
            }, new { message = result.Message ?? "No pudimos completar la importación. Inténtalo nuevamente." });
        }
        catch (EmployeeExcelFormatException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
