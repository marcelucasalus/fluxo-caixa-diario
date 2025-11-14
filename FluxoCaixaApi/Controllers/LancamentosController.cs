using Contract.Command;
using Contract.Query;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxoCaixaApi.Controllers
{
    [ApiController]
    [Produces("application/json")]
    [ApiVersion("1.0")]
    [Route("v{version:apiVersion}/[controller]")]
    public class LancamentosController : ControllerBase
    {
        private readonly IMediator _mediator;

        public LancamentosController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Lancamentos([FromBody] LancamentosCommand request)
        {
            var resultado = await _mediator.Send(request);
            return Ok(resultado);
        }

        [HttpGet]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Lancamentos([FromQuery] DateTime? request)
        {
            try
            {
                var resultado = await _mediator.Send(new LancamentoQuery() { Data = (DateTime)(request == null ? DateTime.Now.Date : request?.Date) });
                return Ok(resultado);
            } catch (Exception ex)
            {
                return NotFound(ex.Message);
            }
            
        }

    }
}
