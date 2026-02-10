using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using StackExchange.Redis;
using Xunit;
using CommandStore.FluxoCaixa;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;

namespace TestesUnitarios.CommandStore
{
    public class FluxoCaixaCommandStoreTestes
    {
        private readonly FluxoCaixaContext _context;
        private readonly Mock<IConnectionMultiplexer> _mockRedis;
        private readonly Mock<IDatabase> _mockDatabase;
        private readonly Mock<IRabbitMqPublisher> _mockRabbitPublisher;
        private readonly Mock<ILogger<FluxoCaixaCommandStore>> _mockLogger;
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly HttpClient _httpClient;
        private readonly FluxoCaixaCommandStore _commandStore;
        private readonly Meter _meter;
        private readonly ActivitySource _activitySource;
        private static readonly Random _randomId = new();

        public FluxoCaixaCommandStoreTestes()
        {
            var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new FluxoCaixaContext(options);
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockDatabase = new Mock<IDatabase>();
            _mockRabbitPublisher = new Mock<IRabbitMqPublisher>();
            _mockLogger = new Mock<ILogger<FluxoCaixaCommandStore>>();
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
            _meter = new Meter("FluxoCaixa.Tests");
            _activitySource = new ActivitySource("FluxoCaixa.Tests");

            _mockRedis
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockDatabase.Object);

            _mockDatabase
                .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            _commandStore = new FluxoCaixaCommandStore(
                _context,
                _mockRedis.Object,
                _mockRabbitPublisher.Object,
                _mockLogger.Object,
                _httpClient,
                _activitySource,
                _meter);
        }

        #region Helper Methods

        private static Lancamento CriarLancamentoTeste(
            int? id = null,
            char tipo = 'C',
            decimal valor = 100,
            DateTime? dataLancamento = null,
            string status = "Pendente",
            string descricao = "Teste")
        {
            return new Lancamento
            {
                IdLancamento = id ?? _randomId.Next(1, 10000),
                Tipo = tipo,
                Valor = valor,
                DataLancamento = dataLancamento ?? DateTime.Now.Date,
                Status = status,
                Descricao = descricao,
                DataConsolidacao = (dataLancamento ?? DateTime.Now).Date
            };
        }

        private void ConfigurarHttpClientMockDisponivel()
        {
            _mockHttpMessageHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK
                });
        }

        private void ConfigurarHttpClientMockIndisponivel()
        {
            _mockHttpMessageHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Serviço indisponível"));
        }

        #endregion

        #region Criação de Consolidado

        [Fact]
        public async Task RegistrarLancamentos_QuandoConsolidadoNaoExiste_DeveCriarNovoConsolidado()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockIndisponivel();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.NotEqual(0, resultado);
            var consolidadoCriado = await _context.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
            Assert.NotNull(consolidadoCriado);
            Assert.Equal(0, consolidadoCriado.TotalCreditos);
            Assert.Equal(0, consolidadoCriado.TotalDebitos);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoConsolidadoExiste_NaoDeveCriarNovoConsolidado()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidadoExistente = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 50,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidadoExistente);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockIndisponivel();

            var consolidadosAntes = await _context.ConsolidadosDiarios.CountAsync();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.NotEqual(0, resultado);
            var consolidadosDepois = await _context.ConsolidadosDiarios.CountAsync();
            Assert.Equal(consolidadosAntes, consolidadosDepois);
        }

        #endregion

        #region Serviço Disponível - Consolidação

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoDisponivel_DeveConsolidarCredito()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 50,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.NotEqual(0, resultado);
            var lancamentoRegistrado = await _context.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == lancamento.IdLancamento);
            Assert.NotNull(lancamentoRegistrado);
            Assert.Equal("Consolidado", lancamentoRegistrado.Status);

            var consolidadoAtualizado = await _context.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
            Assert.Equal(150, consolidadoAtualizado.TotalCreditos);
            Assert.Equal(0, consolidadoAtualizado.TotalDebitos);

            _mockRabbitPublisher.Verify(r => r.PublishLancamento(It.IsAny<Lancamento>()), Times.Never);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoDisponivel_DeveConsolidarDebito()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 30
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'D', valor: 50, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.NotEqual(0, resultado);
            var consolidadoAtualizado = await _context.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
            Assert.Equal(0, consolidadoAtualizado.TotalCreditos);
            Assert.Equal(80, consolidadoAtualizado.TotalDebitos);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoDisponivel_DeveMarcarComoConsolidado()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var lancamentoRegistrado = await _context.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == lancamento.IdLancamento);
            Assert.NotNull(lancamentoRegistrado);
            Assert.Equal("Consolidado", lancamentoRegistrado.Status);
        }

        [Theory]
        [InlineData(100.50)]
        [InlineData(250.75)]
        [InlineData(1000.00)]
        public async Task RegistrarLancamentos_ComDiferentesValores_DeveConsolidarCorretamente(decimal valor)
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: valor, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var consolidadoAtualizado = await _context.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
            Assert.Equal(valor, consolidadoAtualizado.TotalCreditos);
        }

        #endregion

        #region Serviço Indisponível - Pendente

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoIndisponivel_DeveMarcarComoPendente()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockIndisponivel();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.NotEqual(0, resultado);
            var lancamentoRegistrado = await _context.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == lancamento.IdLancamento);
            Assert.NotNull(lancamentoRegistrado);
            Assert.Equal("Pendente", lancamentoRegistrado.Status);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoIndisponivel_DevePublicarNaFila()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockIndisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            _mockRabbitPublisher.Verify(r => r.PublishLancamento(It.IsAny<Lancamento>()), Times.Once);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoIndisponivel_NaoDeveAlterarConsolidado()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 100,
                TotalDebitos = 50
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 200, dataLancamento: dataHoje);

            ConfigurarHttpClientMockIndisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var consolidadoAtualizado = await _context.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
            Assert.Equal(100, consolidadoAtualizado.TotalCreditos);
            Assert.Equal(50, consolidadoAtualizado.TotalDebitos);
        }

        #endregion

        #region Limpeza de Cache

        [Fact]
        public async Task RegistrarLancamentos_DeveDeletarChaveLancamentoDoCache()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var chaveEsperada = "lancamento:" + dataHoje.ToString("yyyy-MM-dd");
            _mockDatabase.Verify(
                d => d.KeyDeleteAsync(chaveEsperada, It.IsAny<CommandFlags>()), 
                Times.Once);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoDisponivel_DeveDeletarChaveConsolidadoDoCache()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var chaveConsolidado = "consolidado:" + dataHoje.ToString("yyyy-MM-dd");
            _mockDatabase.Verify(
                d => d.KeyDeleteAsync(chaveConsolidado, It.IsAny<CommandFlags>()), 
                Times.Once);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoServicoIndisponivel_DeveDeletarApenasChaveLancamento()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);

            ConfigurarHttpClientMockIndisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var chaveLancamento = "lancamento:" + dataHoje.ToString("yyyy-MM-dd");
            var chaveConsolidado = "consolidado:" + dataHoje.ToString("yyyy-MM-dd");

            _mockDatabase.Verify(
                d => d.KeyDeleteAsync(chaveLancamento, It.IsAny<CommandFlags>()), 
                Times.Once);

            _mockDatabase.Verify(
                d => d.KeyDeleteAsync(chaveConsolidado, It.IsAny<CommandFlags>()), 
                Times.Never);
        }

        #endregion

        #region Persistência no Banco

        [Fact]
        public async Task RegistrarLancamentos_DeveSalvarLancamentoNoBanco()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(
                id: 999,
                tipo: 'C',
                valor: 100,
                dataLancamento: dataHoje,
                descricao: "Teste de Persistência");

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var lancamentoSalvo = await _context.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == 999);
            Assert.NotNull(lancamentoSalvo);
            Assert.Equal("Teste de Persistência", lancamentoSalvo.Descricao);
            Assert.Equal(100, lancamentoSalvo.Valor);
            Assert.Equal('C', lancamentoSalvo.Tipo);
        }

        [Fact]
        public async Task RegistrarLancamentos_DeveRetornarIdDoLancamento()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var idEsperado = 12345;
            var lancamento = CriarLancamentoTeste(id: idEsperado, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.Equal(idEsperado, resultado);
        }

        #endregion

        #region Tratamento de Erros

        [Fact]
        public async Task RegistrarLancamentos_QuandoOcorreExcecao_DeveRepassarExcecao()
        {
            // Arrange
            var lancamento = CriarLancamentoTeste();

            _mockRedis
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Throws(new Exception("Erro de conexão Redis"));

            // Act & Assert
            var excecao = await Assert.ThrowsAsync<Exception>(
                () => _commandStore.RegistrarLancamentos(lancamento));
            Assert.Contains("Erro de conexão Redis", excecao.Message);
        }

        [Fact]
        public async Task RegistrarLancamentos_QuandoOcorreExcecao_DeveLogarErro()
        {
            // Arrange
            var lancamento = CriarLancamentoTeste();

            _mockRedis
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Throws(new Exception("Erro de teste"));

            // Act
            await Assert.ThrowsAsync<Exception>(
                () => _commandStore.RegistrarLancamentos(lancamento));

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Erro ao registrar lançamento")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Validação de Dados

        [Fact]
        public async Task RegistrarLancamentos_ComValorDecimal_DevePreservarPrecisao()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataHoje,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 123.45m, dataLancamento: dataHoje);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var lancamentoSalvo = await _context.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == lancamento.IdLancamento);
            Assert.Equal(123.45m, lancamentoSalvo.Valor);

            var consolidadoAtualizado = await _context.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
            Assert.Equal(123.45m, consolidadoAtualizado.TotalCreditos);
        }

        [Fact]
        public async Task RegistrarLancamentos_ComDataEspecifica_DeveUsarDataCorreta()
        {
            // Arrange
            var dataEspecifica = new DateTime(2025, 6, 15);
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataEspecifica,
                TotalCreditos = 0,
                TotalDebitos = 0
            };

            await _context.ConsolidadosDiarios.AddAsync(consolidado);
            await _context.SaveChangesAsync();

            var lancamento = CriarLancamentoTeste(dataLancamento: dataEspecifica);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var lancamentoSalvo = await _context.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == lancamento.IdLancamento);
            Assert.Equal(dataEspecifica.Date, lancamentoSalvo.DataLancamento.Date);
        }

        #endregion
    }
}
