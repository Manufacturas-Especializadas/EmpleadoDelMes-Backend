using System.Security.Claims;
using System.Text.Json.Serialization;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/nominations")]
[Authorize(Policy = AccountRoles.Sponsor)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NominationsController(NominationService service) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateNominationRequest(Guid CandidateEmployeeId, NominationQualities Qualities, string? Justification);

    private Guid UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    [HttpGet("active-campaign")]
    public async Task<IActionResult> ActiveCampaign(CancellationToken token) => Respond(await service.GetActiveCampaignAsync(UserId, token));

    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates(CancellationToken token, [FromQuery] string? term = null,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
        Respond(await service.SearchCandidatesAsync(UserId, term, pageNumber, pageSize, token));

    [HttpPost]
    public async Task<IActionResult> Create(CreateNominationRequest request, CancellationToken token) =>
        Respond(await service.CreateAsync(UserId, request.CandidateEmployeeId, request.Qualities, request.Justification, token), 201);

    private IActionResult Respond<T>(NominationResult<T> result, int successStatus = 200) => result.IsSuccess
        ? StatusCode(successStatus, result.Value)
        : StatusCode(result.Error switch
        {
            NominationError.Validation => 400,
            NominationError.Unauthorized => 401,
            NominationError.Forbidden => 403,
            NominationError.NotFound => 404,
            NominationError.NoActiveCampaign or NominationError.Duplicate or NominationError.Concurrency => 409,
            _ => 503
        }, new { message = result.Message ?? "No pudimos completar la operación. Inténtalo nuevamente." });
}
