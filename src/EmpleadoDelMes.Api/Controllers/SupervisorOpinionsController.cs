using System.Security.Claims;
using System.Text.Json.Serialization;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Application.Opinions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/supervisor-opinions")]
[Authorize(Policy = AccountRoles.Sponsor)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupervisorOpinionsController(SupervisorService service) : ControllerBase
{
    private Guid UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OpinionRequest(string? Content, string? Version);
    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns(CancellationToken token, int pageNumber = 1, int pageSize = 20) => Respond(await service.CampaignsAsync(UserId, pageNumber, pageSize, token));
    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates(Guid campaignId, CancellationToken token, string? term = null, bool? hasOpinion = null, int pageNumber = 1, int pageSize = 20) =>
        Respond(await service.CandidatesAsync(UserId, campaignId, term, hasOpinion, pageNumber, pageSize, token));
    [HttpGet("{campaignId}/{candidateEmployeeId}")]
    public async Task<IActionResult> Get(Guid campaignId, Guid candidateEmployeeId, CancellationToken token) => Respond(await service.GetAsync(UserId, campaignId, candidateEmployeeId, token));
    [HttpPut("{campaignId}/{candidateEmployeeId}")]
    public async Task<IActionResult> Save(Guid campaignId, Guid candidateEmployeeId, OpinionRequest request, CancellationToken token)
    {
        var result = await service.SaveAsync(UserId, campaignId, candidateEmployeeId, request.Content, request.Version, token);
        if (!result.IsSuccess) return Respond(result);
        return result.Value!.Created ? CreatedAtAction(nameof(Get), new { campaignId, candidateEmployeeId }, result.Value.Opinion) : Ok(result.Value.Opinion);
    }
    private IActionResult Respond<T>(NominationResult<T> r) => r.IsSuccess ? Ok(r.Value) : StatusCode(r.Error switch
    { NominationError.Validation => 400, NominationError.NotFound => 404, NominationError.Forbidden => 403, NominationError.Concurrency => 409, _ => 503 }, new { message = r.Message });
}

[ApiController]
[Route("api/admin/preselections")]
[Authorize(Policy = AccountPolicies.RecognitionManagement)]
public sealed class EvaluatorLinksController(SupervisorService service) : ControllerBase
{
    [HttpPost("{campaignId}/{candidateEmployeeId}/evaluator-link")]
    public async Task<IActionResult> Link(Guid campaignId, Guid candidateEmployeeId, CancellationToken token)
    {
        var r = await service.LinkAsync(campaignId, candidateEmployeeId, token);
        return r.IsSuccess ? Ok(r.Value) : StatusCode(r.Error switch
        { NominationError.Validation => 400, NominationError.NotFound => 404, NominationError.Concurrency => 409, _ => 503 }, new { message = r.Message });
    }
}
