using Microsoft.AspNetCore.Mvc;

namespace EmpleadoDelMes.Api.Controllers;

[ApiController]
[Route("api/ping")]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            message = "EmpleadoDelMes API funcionando"
        });
    }
}
