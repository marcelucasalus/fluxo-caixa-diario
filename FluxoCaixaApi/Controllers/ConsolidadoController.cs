using Contract.Query;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FluxoCaixaApi.Controllers
{
    [ApiController]
    [Produces("application/json")]
    [ApiVersion("1.0")]
    [Route("v{version:apiVersion}/[controller]")]
    public class ConsolidadoController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ConsolidadoController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet]
        public async Task<IActionResult> ConsolidadoDiario([FromQuery] DateTime? request)
        {
            try
            {
                var resultado = await _mediator.Send(new ConsolidadoQuery() { Data = (DateTime)(request == null ? DateTime.Now.Date : request?.Date) });
                return Ok(resultado);
            }
            catch (Exception ex) 
            { 
                return NotFound(ex.Message);
            }
        }
            
    }
}