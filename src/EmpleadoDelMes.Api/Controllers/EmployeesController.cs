
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize(Policy = AccountRoles.Administrator)]
public sealed class EmployeesController(
    EmployeeService employeeService) : ControllerBase
{
    public sealed record CreateEmployeeRequest(
        string? PayrollNumber,
        string? FullName);

    public sealed record UpdateEmployeeNameRequest(
        string? FullName,
        string? Version);

    [HttpGet]
    public async Task<IActionResult> Search(
        CancellationToken cancellationToken,
        [FromQuery] string? term = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await employeeService.SearchEmployees(
            term,
            pageNumber,
            pageSize,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Error(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await employeeService.GetEmployeeById(
            id, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Error(result);
    }

    [HttpGet("by-payroll/{payrollNumber}")]
    public async Task<IActionResult> GetByPayrollNumber(
        string payrollNumber,
        CancellationToken cancellationToken)
    {
        var result = await employeeService.GetEmployeeByPayrollNumber(
            payrollNumber, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Error(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await employeeService.CreateEmployee(
            request.PayrollNumber,
            request.FullName,
            cancellationToken);

        if (!result.IsSuccess)
            return Error(result);

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            result.Value);
    }

    [HttpPut("{id:guid}/name")]
    public async Task<IActionResult> UpdateName(
        Guid id,
        [FromBody] UpdateEmployeeNameRequest request,
        CancellationToken cancellationToken)
    {
        var result = await employeeService.UpdateEmployeeName(
            id,
            request.FullName,
            request.Version,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Error(result);
    }

    private IActionResult Error<T>(EmployeeResult<T> result)
    {
        var statusCode = result.Error switch
        {
            EmployeeError.Validation => 400,
            EmployeeError.NotFound => 404,
            EmployeeError.Duplicate => 409,
            EmployeeError.Concurrency => 409,
            EmployeeError.Persistence => 503,
            _ => 503
        };

        return StatusCode(statusCode, new
        {
            message = result.Message ??
                "No pudimos completar la operación. Inténtalo nuevamente."
        });
    }
}
