using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Campaigns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/opinions/campaigns")]
[Authorize(Roles = AccountRoles.HumanResources + "," + AccountRoles.Safety + "," + AccountRoles.Quality)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OpinionCampaignsController(CampaignService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        if (User.IsInRole(AccountRoles.Administrator) || User.IsInRole(AccountRoles.Sponsor))
            return StatusCode(403, new { message = "Esta consulta está disponible únicamente para cuentas departamentales." });
        var result = await service.ListForOpinionsAsync(pageNumber, pageSize, token);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error == CampaignError.Validation ? 400 : 503,
            new { message = result.Message });
    }
}
