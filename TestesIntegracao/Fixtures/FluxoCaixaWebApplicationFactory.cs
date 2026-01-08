using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CommandStore.FluxoCaixa;
using Store.Identity;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestesIntegracao.Fixtures
{
    public class FluxoCaixaWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove banco de dados existente
                var descriptor = services.ServiceDescriptor(
                    typeof(DbContextOptions<FluxoCaixaContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Adiciona banco de dados em memória para testes
                services.AddDbContext<FluxoCaixaContext>(options =>
                {
                    options.UseInMemoryDatabase("TestDB_" + Guid.NewGuid());
                });

                // Build e migrações
                var serviceProvider = services.BuildServiceProvider();
                using (var scope = serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<FluxoCaixaContext>();
                    context.Database.EnsureCreated();

                    // Seed dados de teste
                    SeedTestData(context, scope.ServiceProvider).Wait();
                }
            });
        }

        private async Task SeedTestData(FluxoCaixaContext context, IServiceProvider serviceProvider)
        {
            if (await context.Users.AnyAsync())
                return;

            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            
            // Criar usuário de teste
            var testUser = new ApplicationUser
            {
                UserName = "testeadmin",
                Email = "testeadmin@test.com",
                FullName = "Admin Teste"
            };

            var result = await userManager.CreateAsync(testUser, "Teste@123456");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(testUser, "Admin");
            }

            // Criar consolidado de teste
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = DateTime.Now.Date,
                TotalCreditos = 0,
                TotalDebitos = 0
            };
            context.ConsolidadosDiarios.Add(consolidado);
            await context.SaveChangesAsync();
        }
    }
}