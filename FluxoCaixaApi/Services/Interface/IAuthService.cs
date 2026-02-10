using Contract.Dto;

namespace FluxoCaixaApi.Services.Interface
{
    public interface IAuthService
    {
        Task<(bool Success, string Message, IEnumerable<string> Errors)> RegisterAsync(RegisterDto dto);
        Task<(bool Success, string Token)> LoginAsync(LoginDto dto);
    }
}
