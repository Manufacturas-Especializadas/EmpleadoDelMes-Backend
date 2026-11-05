using System.Security.Claims;
using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.CampaignAutomation;
using EmpleadoDelMes.Application.Campaigns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/admin/campaign-automation")]
[Authorize(Policy = AccountRoles.Administrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CampaignAutomationController(CampaignAutomationService service) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken token) => Respond(() => service.GetAsync(token));

    [HttpPut]
    public Task<IActionResult> Update(AutomationUpdateRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty)
            return Task.FromResult<IActionResult>(Unauthorized());
        return Respond(() => service.UpdateAsync(request, actor, token));
    }

    [HttpPost("preview")]
    public Task<IActionResult> Preview(AutomationPreviewRequest request, CancellationToken token) =>
        Respond(() => service.PreviewAsync(request, token));

    private async Task<IActionResult> Respond<T>(Func<Task<T>> operation)
    {
        try { return Ok(await operation()); }
        catch (AutomationException ex)
        {
            return StatusCode(ex.Error switch
            {
                CampaignError.Validation => 400,
                CampaignError.NotFound => 404,
                CampaignError.Concurrency or CampaignError.Duplicate => 409,
                _ => 503
            }, new { message = ex.Message });
        }
    }
}
