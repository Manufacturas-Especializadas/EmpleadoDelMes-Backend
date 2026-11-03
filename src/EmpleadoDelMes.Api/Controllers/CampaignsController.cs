using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Campaigns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EmpleadoDelMes.Api.Serialization;
using System.Text.Json.Serialization;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CampaignsController(CampaignService service) : ControllerBase
{
    public sealed record VersionRequest(string? Version);
    public sealed record ScheduleRequest(string? Version,
        [property: JsonConverter(typeof(ExplicitOffsetDateTimeConverter))] DateTimeOffset? OpeningAt,
        [property: JsonConverter(typeof(ExplicitOffsetDateTimeConverter))] DateTimeOffset? ClosingAt);

    [HttpPut("{id}/nominations/schedule")]
    public async Task<IActionResult> ScheduleNominations(Guid id, ScheduleRequest request, CancellationToken token) =>
        Respond(await service.ChangeAsync(id, request.Version, CampaignChange.ScheduleNominations, request.OpeningAt, request.ClosingAt, token));

    [HttpPut("{id}/opinions/schedule")]
    public async Task<IActionResult> ScheduleOpinions(Guid id, ScheduleRequest request, CancellationToken token) =>
        Respond(await service.ChangeAsync(id, request.Version, CampaignChange.ScheduleOpinions, request.OpeningAt, request.ClosingAt, token));

    [HttpPost("{id}/nominations/open")]
    public Task<IActionResult> OpenNominations(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.OpenNominations, token);
    [HttpPost("{id}/nominations/close")]
    public Task<IActionResult> CloseNominations(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.CloseNominations, token);
    [HttpPost("{id}/nominations/automatic")]
    public Task<IActionResult> AutomaticNominations(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.AutomaticNominations, token);
    [HttpPost("{id}/opinions/open")]
    public Task<IActionResult> OpenOpinions(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.OpenOpinions, token);
    [HttpPost("{id}/opinions/close")]
    public Task<IActionResult> CloseOpinions(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.CloseOpinions, token);
    [HttpPost("{id}/opinions/automatic")]
    public Task<IActionResult> AutomaticOpinions(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.AutomaticOpinions, token);
    [HttpPost("{id}/archive")]
    public Task<IActionResult> Archive(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.Archive, token);
    [HttpPost("{id}/reopen")]
    public Task<IActionResult> Reopen(Guid id, VersionRequest request, CancellationToken token) => Change(id, request, CampaignChange.Reopen, token);

    private async Task<IActionResult> Change(Guid id, VersionRequest request, CampaignChange change, CancellationToken token) =>
        Respond(await service.ChangeAsync(id, request.Version, change, cancellationToken: token));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
        Respond(await service.ListAsync(pageNumber, pageSize, cancellationToken));

    // Guid binding produces 400 for malformed identifiers instead of a route-level 404.
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Respond(await service.GetByIdAsync(id, cancellationToken));

    private IActionResult Respond<T>(CampaignResult<T> result) => result.IsSuccess ? Ok(result.Value) :
        StatusCode(result.Error switch
        {
            CampaignError.Validation => 400,
            CampaignError.NotFound => 404,
            CampaignError.Concurrency or CampaignError.Duplicate => 409,
            _ => 503
        }, new { message = result.Message ?? "No pudimos consultar las convocatorias. Inténtalo nuevamente." });
}
