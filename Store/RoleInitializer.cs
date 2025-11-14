using Microsoft.AspNetCore.Identity;

namespace Store
{
    public static class RoleInitializer
    {
        public static async Task InitializeAsync(RoleManager<IdentityRole> roleManager)
        {
            if (!await roleManager.RoleExistsAsync("admin"))
                await roleManager.CreateAsync(new IdentityRole("admin"));
            if (!await roleManager.RoleExistsAsync("consulta"))
                await roleManager.CreateAsync(new IdentityRole("consulta"));
        }
    }
}
