using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Candidates;
using EmpleadoDelMes.Application.Nominations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/admin/candidates")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCandidatesController(CandidateReviewService service) : ControllerBase
{
    [HttpGet("review")]
    public async Task<IActionResult> Search(CancellationToken token, [FromQuery] Guid? campaignId = null,
        [FromQuery] string? term = null, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
        Respond(await service.SearchAsync(campaignId, term, pageNumber, pageSize, token));

    [HttpGet("{campaignId}/{candidateEmployeeId}/review")]
    public async Task<IActionResult> Get(Guid campaignId, Guid candidateEmployeeId, CancellationToken token) =>
        Respond(await service.GetAsync(campaignId, candidateEmployeeId, token));

    private IActionResult Respond<T>(NominationResult<T> result) => result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error switch
    {
        NominationError.Validation => 400,
        NominationError.NotFound => 404,
        _ => 503
    }, new { message = result.Message });
}
