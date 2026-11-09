using System.Security.Claims;
using System.Text.Json.Serialization;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Application.Preselections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/admin/preselections")]
[Authorize(Policy = AccountPolicies.RecognitionManagement)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPreselectionsController(PreselectionService service) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ChangeRequest(string? Version);

    [HttpPost("{campaignId}/{candidateEmployeeId}/select")]
    public Task<IActionResult> Select(Guid campaignId, Guid candidateEmployeeId, ChangeRequest request,
        CancellationToken token) => Change(campaignId, candidateEmployeeId, request, true, token);

    [HttpPost("{campaignId}/{candidateEmployeeId}/withdraw")]
    public Task<IActionResult> Withdraw(Guid campaignId, Guid candidateEmployeeId, ChangeRequest request,
        CancellationToken token) => Change(campaignId, candidateEmployeeId, request, false, token);

    private async Task<IActionResult> Change(Guid campaignId, Guid candidateId, ChangeRequest request, bool select,
        CancellationToken token)
    {
        var actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        var result = await service.ChangeAsync(campaignId, candidateId, actor, select, request.Version, token);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error switch
        {
            NominationError.Validation => 400,
            NominationError.Unauthorized => 401,
            NominationError.Forbidden => 403,
            NominationError.NotFound => 404,
            NominationError.Concurrency => 409,
            _ => 503
        }, new { message = result.Message });
    }
}
