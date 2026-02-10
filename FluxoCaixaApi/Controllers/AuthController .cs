using Contract.Dto;
using FluxoCaixaApi.Services.Interface;
using Microsoft.AspNetCore.Mvc;

namespace FluxoCaixaApi.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var result = await _authService.RegisterAsync(dto);

            if (!result.Success)
            {
                if (result.Errors.Any())
                {
                    return BadRequest(result.Errors);
                }
                return BadRequest(result.Message);
            }

            return Ok(new { message = result.Message });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var result = await _authService.LoginAsync(dto);

            if (!result.Success)
            {
                return Unauthorized();
            }

            return Ok(new { token = result.Token });
        }
    }
}
