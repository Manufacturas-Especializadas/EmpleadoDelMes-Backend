
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json.Serialization;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/admin/nominations")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AdminNominationsController(
    NominationAdminService service) : ControllerBase
{
    // A literal route takes precedence over the existing {id} detail route.
    [HttpGet("candidates")]
    public async Task<IActionResult> SearchCandidates(
        CancellationToken cancellationToken,
        [FromQuery] Guid? campaignId = null,
        [FromQuery] string? term = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20) =>
        Respond(await service.SearchCandidatesAsync(campaignId, term, pageNumber, pageSize, cancellationToken));

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CorrectionRequest(string? Version, NominationQualities Qualities, string? Justification, string? Reason);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CancellationRequest(string? Version, string? Reason);

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationRequest request, CancellationToken cancellationToken)
    {
        var actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty;
        return Respond(await service.CancelAsync(id, actor, request.Version, request.Reason, cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Respond(await service.GetByIdAsync(id, cancellationToken));

    [HttpPut("{id}/correction")]
    public async Task<IActionResult> Correct(Guid id, CorrectionRequest request, CancellationToken cancellationToken)
    {
        var actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty;
        return Respond(await service.CorrectAsync(id, actor, request.Version, request.Qualities,
            request.Justification, request.Reason, cancellationToken));
    }

    private IActionResult Respond<T>(NominationResult<T> result) => result.IsSuccess ? Ok(result.Value) :
        StatusCode(result.Error switch
        {
            NominationError.Validation => 400,
            NominationError.Unauthorized => 401,
            NominationError.Forbidden => 403,
            NominationError.NotFound => 404,
            NominationError.Concurrency or NominationError.Duplicate => 409,
            _ => 503
        }, new { message = result.Message ?? "No pudimos completar la operación. Inténtalo nuevamente." });

    [HttpGet]
    public async Task<IActionResult> Search(
        CancellationToken cancellationToken,
        [FromQuery] Guid? campaignId = null,
        [FromQuery] string? term = null,
        [FromQuery] bool? isCancelled = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await service.SearchAsync(
            campaignId,
            term,
            isCancelled,
            pageNumber,
            pageSize,
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var statusCode = result.Error switch
        {
            NominationError.Validation => 400,
            NominationError.NotFound => 404,
            _ => 503
        };

        return StatusCode(statusCode, new
        {
            message = result.Message ??
                "No pudimos consultar las postulaciones. " +
                "Inténtalo nuevamente."
        });
    }
}
