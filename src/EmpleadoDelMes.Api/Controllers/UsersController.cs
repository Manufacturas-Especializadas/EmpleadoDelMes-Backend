
using EmpleadoDelMes.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(
    IAccountService accountService,
    IAccountDirectory accountDirectory) : ControllerBase
{
    public sealed record CreateSponsorRequest(Guid EmployeeId);

    public sealed record CreateDepartmentRequest(string? Role);

    // Crear cuenta para un encargado o jefe.
    [HttpPost("sponsors")]
    public async Task<IActionResult> CreateSponsor(
        [FromBody] CreateSponsorRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.CreateSponsorAsync(
            request.EmployeeId,
            cancellationToken);

        return CreatedAccountResponse(result);
    }

    // Crear cuenta compartida para RH, Seguridad o Calidad.
    [HttpPost("departments")]
    public async Task<IActionResult> CreateDepartment(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var userName = request.Role switch
        {
            AccountRoles.HumanResources => "rh",
            AccountRoles.Safety => "seguridad",
            AccountRoles.Quality => "calidad",
            _ => null
        };

        if (userName is null)
        {
            return BadRequest(new
            {
                message = "Selecciona un departamento válido: " +
                          "Recursos Humanos, Seguridad o Calidad."
            });
        }

        var result = await accountService.CreateAdministrativeAsync(
            userName,
            request.Role!,
            cancellationToken);

        return CreatedAccountResponse(result);
    }

    // Consultar una cuenta sin revelar información sensible.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await accountService.GetAccountAsync(
            id,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Error(result);
    }


    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? term,
        [FromQuery] string? role,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await accountDirectory.SearchAsync(
            term,
            role,
            pageNumber,
            pageSize,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Error(result);
    }


    // La contraseña temporal se entrega una sola vez.
    private IActionResult CreatedAccountResponse(
        AccountResult<CreatedAccount> result)
    {
        if (!result.IsSuccess)
        {
            return Error(result);
        }

        var created = result.Value!;

        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";

        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Account.Id },
            new
            {
                account = created.Account,
                temporaryPassword = created.TemporaryPassword,
                message = "La cuenta se creó correctamente. " +
                          "Guarda la contraseña temporal y entrégala " +
                          "de forma privada al usuario."
            });
    }

    private IActionResult Error<T>(AccountResult<T> result)
    {
        var statusCode = result.Error switch
        {
            AccountError.Validation => 400,
            AccountError.NotFound => 404,
            AccountError.Duplicate => 409,
            AccountError.Concurrency => 409,
            AccountError.InvalidCredentials => 401,
            AccountError.Persistence => 503,
            _ => 503
        };

        return StatusCode(statusCode, new
        {
            message = result.Message ??
                "No pudimos completar la operación. " +
                "Inténtalo nuevamente."
        });
    }
}
