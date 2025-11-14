using CommandStore.FluxoCaixa;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FluxoCaixaApi.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly FluxoCaixaContext _context;

    public HealthController(FluxoCaixaContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> HealthConsolidado()
    {
        try
        {
            var existe = await _context.ConsolidadosDiarios.AnyAsync();

            return Ok(new { status = "UP" });
        }
        catch
        {
            return StatusCode(503, new { status = "DOWN" });
        }
    }
}

