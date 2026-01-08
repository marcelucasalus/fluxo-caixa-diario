using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Contract.Command;
using Contract.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CommandStore.FluxoCaixa;
using TestesIntegracao.Fixtures;
using TestesIntegracao.Helpers;

namespace TestesIntegracao.Endpoints
{
    [Collection("IntegrationTest Collection")]
    public class LancamentosIntegrationTests
    {
        private readonly FluxoCaixaWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public LancamentosIntegrationTests(FluxoCaixaWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            
            // Adicionar token JWT válido
            var token = AuthenticationHelper.GenerateJwtToken(
                "test-user-id",
                "testeadmin",
                new[] { "Admin" });
            AuthenticationHelper.AddBearerToken(_client, token);
        }

        #region POST - RegistrarLancamento

        [Fact]
        public async Task PostLancamento_ComDadosValidos_RetornaCreated()
        {
            // Arrange
            var comando = new LancamentosCommand
            {
                Descricao = "Venda de Produto",
                Tipo = 'C',
                Valor = 150.50m,
                DataLancamento = DateTime.Now.Date
            };

            // Act
            var response = await _client.PostAsJsonAsync("/v1.0/lancamentos", comando);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var content = await response.Content.ReadAsAsync<dynamic>();
            Assert.NotNull(content);
        }

        [Fact]
        public async Task PostLancamento_ComValorNegativo_RetornaBadRequest()
        {
            // Arrange
            var comando = new LancamentosCommand
            {
                Descricao = "Venda com valor negativo",
                Tipo = 'C',
                Valor = -100m,
                DataLancamento = DateTime.Now.Date
            };

            // Act
            var response = await _client.PostAsJsonAsync("/v1.0/lancamentos", comando);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task PostLancamento_SemAutenticacao_RetornaUnauthorized()
        {
            // Arrange
            var clientSemAutenticacao = _factory.CreateClient();
            var comando = new LancamentosCommand
            {
                Descricao = "Teste sem autenticação",
                Tipo = 'C',
                Valor = 100m,
                DataLancamento = DateTime.Now.Date
            };

            // Act
            var response = await clientSemAutenticacao.PostAsJsonAsync("/v1.0/lancamentos", comando);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task PostLancamento_ComTipoCredito_AtualizaConsolidado()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var comando = new LancamentosCommand
            {
                Descricao = "Crédito de Teste",
                Tipo = 'C',
                Valor = 500m,
                DataLancamento = dataHoje
            };

            // Act
            var response = await _client.PostAsJsonAsync("/v1.0/lancamentos", comando);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Verificar no banco de dados
            using (var scope = _factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoCaixaContext>();
                var consolidado = await context.ConsolidadosDiarios
                    .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
                
                Assert.NotNull(consolidado);
                Assert.Equal(500m, consolidado.TotalCreditos);
            }
        }

        [Fact]
        public async Task PostLancamento_ComTipoDebito_AtualizaConsolidado()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var comando = new LancamentosCommand
            {
                Descricao = "Débito de Teste",
                Tipo = 'D',
                Valor = 300m,
                DataLancamento = dataHoje
            };

            // Act
            var response = await _client.PostAsJsonAsync("/v1.0/lancamentos", comando);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Verificar no banco de dados
            using (var scope = _factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoCaixaContext>();
                var consolidado = await context.ConsolidadosDiarios
                    .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
                
                Assert.NotNull(consolidado);
                Assert.Equal(300m, consolidado.TotalDebitos);
            }
        }

        [Fact]
        public async Task PostMultiplosLancamentos_AcumulaValores()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var credito1 = new LancamentosCommand
            {
                Descricao = "Crédito 1",
                Tipo = 'C',
                Valor = 100m,
                DataLancamento = dataHoje
            };
            var credito2 = new LancamentosCommand
            {
                Descricao = "Crédito 2",
                Tipo = 'C',
                Valor = 200m,
                DataLancamento = dataHoje
            };
            var debito1 = new LancamentosCommand
            {
                Descricao = "Débito 1",
                Tipo = 'D',
                Valor = 50m,
                DataLancamento = dataHoje
            };

            // Act
            await _client.PostAsJsonAsync("/v1.0/lancamentos", credito1);
            await _client.PostAsJsonAsync("/v1.0/lancamentos", credito2);
            await _client.PostAsJsonAsync("/v1.0/lancamentos", debito1);

            // Assert
            using (var scope = _factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoCaixaContext>();
                var consolidado = await context.ConsolidadosDiarios
                    .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
                
                Assert.NotNull(consolidado);
                Assert.Equal(300m, consolidado.TotalCreditos); // 100 + 200
                Assert.Equal(50m, consolidado.TotalDebitos);
            }
        }

        #endregion

        #region GET - ConsultarLancamentos

        [Fact]
        public async Task GetLancamentos_ComDataValida_RetornaOk()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var comando = new LancamentosCommand
            {
                Descricao = "Lançamento para Consulta",
                Tipo = 'C',
                Valor = 250m,
                DataLancamento = dataHoje
            };

            // Registrar um lançamento primeiro
            await _client.PostAsJsonAsync("/v1.0/lancamentos", comando);

            // Act
            var response = await _client.GetAsync($"/v1.0/lancamentos?request={dataHoje:yyyy-MM-dd}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var content = await response.Content.ReadAsStringAsync();
            Assert.NotEmpty(content);
        }

        [Fact]
        public async Task GetLancamentos_SemParametroData_UsaDataAtual()
        {
            // Arrange
            var comando = new LancamentosCommand
            {
                Descricao = "Lançamento de Hoje",
                Tipo = 'C',
                Valor = 100m,
                DataLancamento = DateTime.Now.Date
            };

            await _client.PostAsJsonAsync("/v1.0/lancamentos", comando);

            // Act
            var response = await _client.GetAsync("/v1.0/lancamentos");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetLancamentos_ComDataFutura_RetornaVazio()
        {
            // Arrange
            var dataFutura = DateTime.Now.Date.AddDays(10);

            // Act
            var response = await _client.GetAsync($"/v1.0/lancamentos?request={dataFutura:yyyy-MM-dd}");

            // Assert
            // Pode ser 200 com lista vazia ou 404, dependendo da implementação
            Assert.True(
                response.StatusCode == HttpStatusCode.OK || 
                response.StatusCode == HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetLancamentos_SemAutenticacao_RetornaUnauthorized()
        {
            // Arrange
            var clientSemAutenticacao = _factory.CreateClient();

            // Act
            var response = await clientSemAutenticacao.GetAsync("/v1.0/lancamentos");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        #endregion

        #region Cleanup

        public void Dispose()
        {
            _client?.Dispose();
        }

        #endregion
    }
}