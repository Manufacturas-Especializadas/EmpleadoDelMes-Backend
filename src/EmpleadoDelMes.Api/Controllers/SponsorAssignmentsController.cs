using System.Text.Json.Serialization;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Assignments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/admin/sponsor-assignments")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SponsorAssignmentsController(AssignmentService service) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AssignRequest(Guid SponsorEmployeeId);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReassignRequest(Guid SponsorEmployeeId, string? Version);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record WithdrawRequest(string? Version);

    [HttpGet("sponsors")]
    public async Task<IActionResult> Sponsors(CancellationToken token, string? term = null, int pageNumber = 1, int pageSize = 20) =>
        Respond(await service.SearchAsync(AssignmentSearchKind.Sponsors, null, term, pageNumber, pageSize, token));
    [HttpGet("sponsors/{sponsorId}/employees")]
    public async Task<IActionResult> Collaborators(Guid sponsorId, CancellationToken token, string? term = null, int pageNumber = 1, int pageSize = 20) =>
        Respond(await service.SearchAsync(AssignmentSearchKind.Collaborators, sponsorId, term, pageNumber, pageSize, token));
    [HttpGet("employees/available")]
    public async Task<IActionResult> Available(CancellationToken token, Guid? sponsorId = null, string? term = null, int pageNumber = 1, int pageSize = 20) =>
        Respond(await service.SearchAsync(AssignmentSearchKind.Available, sponsorId, term, pageNumber, pageSize, token));
    [HttpGet("employees/pending")]
    public async Task<IActionResult> Pending(CancellationToken token, string? term = null, int pageNumber = 1, int pageSize = 20) =>
        Respond(await service.SearchAsync(AssignmentSearchKind.Pending, null, term, pageNumber, pageSize, token));
    [HttpGet("employees/{employeeId}/current")]
    public async Task<IActionResult> Current(Guid employeeId, CancellationToken token)
    {
        var result = await service.CurrentAsync(employeeId, token);
        return result.IsSuccess ? Ok(new { assignment = result.Value }) : Respond(result);
    }
    [HttpPost("employees/{employeeId}/assign")]
    public async Task<IActionResult> Assign(Guid employeeId, AssignRequest request, CancellationToken token)
    {
        var result = await service.ChangeAsync(employeeId, request.SponsorEmployeeId, AssignmentChange.Assign, null, token);
        return result.IsSuccess ? CreatedAtAction(nameof(Current), new { employeeId }, result.Value) : Respond(result);
    }
    [HttpPut("employees/{employeeId}/reassign")]
    public async Task<IActionResult> Reassign(Guid employeeId, ReassignRequest request, CancellationToken token) =>
        Respond(await service.ChangeAsync(employeeId, request.SponsorEmployeeId, AssignmentChange.Reassign, request.Version, token));
    [HttpPost("employees/{employeeId}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid employeeId, WithdrawRequest request, CancellationToken token) =>
        Respond(await service.ChangeAsync(employeeId, null, AssignmentChange.Withdraw, request.Version, token));
    private IActionResult Respond<T>(AssignmentResult<T> result) => result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error switch
    {
        AssignmentError.Validation => 400, AssignmentError.NotFound => 404, AssignmentError.Conflict => 409, _ => 503
    }, new { message = result.Message });
}
