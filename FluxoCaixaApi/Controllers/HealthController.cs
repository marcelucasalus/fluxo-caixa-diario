using CommandStore.FluxoCaixa;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QueryStore;

namespace FluxoCaixaApi.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly FluxoCaixaContext _context;
    private readonly ILogger<ConsolidadoQueryStore> _logger;

    public HealthController(FluxoCaixaContext context, ILogger<ConsolidadoQueryStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> HealthConsolidado()
    {
        try
        {
            var existe = await _context.ConsolidadosDiarios.AnyAsync();
            _logger.LogInformation("Api funcionando...");
            return Ok(new { status = "UP" });
        }
        catch
        {
            _logger.LogInformation("Houve uma instabilidade na api");
            return StatusCode(503, new { status = "DOWN" });
        }
    }
}

