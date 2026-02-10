using Contract.Dto;
using FluxoCaixaApi.Services.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Store.Identity;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace FluxoCaixaApi.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _config;

        public AuthService(
            UserManager<ApplicationUser> userManager, 
            RoleManager<IdentityRole> roleManager, 
            IConfiguration config)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _config = config;
        }

        public async Task<(bool Success, string Message, IEnumerable<string> Errors)> RegisterAsync(RegisterDto dto)
        {
            if (dto.Role != "admin" && dto.Role != "consulta")
            {
                return (false, "Role inválida. Use 'admin' ou 'consulta'.", Enumerable.Empty<string>());
            }

            var user = new ApplicationUser
            {
                UserName = dto.Email,
                Email = dto.Email
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
            {
                return (false, string.Empty, result.Errors.Select(e => e.Description));
            }

            // garante que a role exista (em geral já inicializada, mas redundante)
            if (!await _roleManager.RoleExistsAsync(dto.Role))
            {
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));
            }

            await _userManager.AddToRoleAsync(user, dto.Role);

            return (true, "Usuário criado com sucesso", Enumerable.Empty<string>());
        }

        public async Task<(bool Success, string Token)> LoginAsync(LoginDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                return (false, string.Empty);
            }

            if (!await _userManager.CheckPasswordAsync(user, dto.Password))
            {
                return (false, string.Empty);
            }

            var roles = await _userManager.GetRolesAsync(user);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("consolidado_id", user.ConsolidadoId.ToString())
            };

            foreach (var r in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, r));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: creds
            );

            return (true, new JwtSecurityTokenHandler().WriteToken(token));
        }
    }
}
