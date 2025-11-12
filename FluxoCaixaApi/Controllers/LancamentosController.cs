using Contract.Command;
using MediatR;
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

        [HttpPost("registrarLancamento")]
        public async Task<IActionResult> RegistrarLancamento()
        {
            var resultado = await _mediator.Send(new LancamentoRegistrarCommand());
            return Ok(resultado);
        }
    }
}
