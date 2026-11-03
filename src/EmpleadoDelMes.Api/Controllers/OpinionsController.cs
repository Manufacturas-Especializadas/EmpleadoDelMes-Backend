using System.Security.Claims;
using System.Text.Json.Serialization;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Application.Opinions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/opinions")]
[Authorize(Roles = AccountRoles.HumanResources + "," + AccountRoles.Safety + "," + AccountRoles.Quality)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OpinionsController(OpinionService service) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OpinionRequest(string? Content, string? Version);

    private OpinionActor Actor() => new(
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
        new[] { AccountRoles.Administrator, AccountRoles.Sponsor, AccountRoles.HumanResources,
            AccountRoles.Safety, AccountRoles.Quality }.Where(User.IsInRole).ToArray());

    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates(CancellationToken token, [FromQuery] Guid? campaignId = null,
        [FromQuery] string? term = null, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
        Respond(await service.SearchAsync(Actor(), campaignId, term, pageNumber, pageSize, token));

    [HttpGet("{campaignId}/{candidateEmployeeId}")]
    public async Task<IActionResult> Get(Guid campaignId, Guid candidateEmployeeId, CancellationToken token) =>
        Respond(await service.GetAsync(Actor(), campaignId, candidateEmployeeId, token));

    [HttpPut("{campaignId}/{candidateEmployeeId}")]
    public async Task<IActionResult> Save(Guid campaignId, Guid candidateEmployeeId, OpinionRequest request, CancellationToken token)
    {
        var result = await service.SaveAsync(Actor(), campaignId, candidateEmployeeId, request.Content, request.Version, token);
        if (!result.IsSuccess) return Respond(result);
        return result.Value!.Created
            ? CreatedAtAction(nameof(Get), new { campaignId, candidateEmployeeId }, result.Value.Opinion)
            : Ok(result.Value.Opinion);
    }

    private IActionResult Respond<T>(NominationResult<T> result) => result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error switch
    {
        NominationError.Validation => 400,
        NominationError.Unauthorized => 401,
        NominationError.Forbidden => 403,
        NominationError.NotFound => 404,
        NominationError.Concurrency => 409,
        _ => 503
    }, new { message = result.Message });
}
