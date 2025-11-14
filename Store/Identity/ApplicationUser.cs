using Microsoft.AspNetCore.Identity;

namespace Store.Identity
{
    public class ApplicationUser : IdentityUser
    {
        public Guid ConsolidadoId { get; set; }
    }
}
