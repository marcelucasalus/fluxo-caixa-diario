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

namespace TestesUnitarios.CommandStore
{
    public class FluxoCaixaCommandStoreTestes
    {
        private readonly FluxoCaixaContext _context;
        private readonly Mock<IConnectionMultiplexer> _mockRedis;
        private readonly Mock<IRabbitMqPublisher> _mockRabbitPublisher;
        private readonly Mock<ILogger<FluxoCaixaCommandStore>> _mockLogger;
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly HttpClient _httpClient;
        private readonly FluxoCaixaCommandStore _commandStore;

        public FluxoCaixaCommandStoreTestes()
        {
            // Criar DbContext em memória
            var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new FluxoCaixaContext(options);
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockRabbitPublisher = new Mock<IRabbitMqPublisher>();
            _mockLogger = new Mock<ILogger<FluxoCaixaCommandStore>>();
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_mockHttpMessageHandler.Object);

            _commandStore = new FluxoCaixaCommandStore(
                _context,
                _mockRedis.Object,
                _mockRabbitPublisher.Object,
                _mockLogger.Object,
                _httpClient);
        }

        #region RegistrarLancamentos - Consolidado não existe

        [Fact]
        public async Task RegistrarLancamentos_QuandoConsolidadoNaoExiste_DeveCriarNovoConsolidado()
        {
            // Arrange
            var dataHoje = DateTime.Now.Date;
            var lancamento = CriarLancamentoTeste(tipo: 'C', valor: 100, dataLancamento: dataHoje);
            var mockDatabase = new Mock<IDatabase>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

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

        #endregion

        #region RegistrarLancamentos - Consolidado existe

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
            var mockDatabase = new Mock<IDatabase>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

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

        #region ValidaLancamento - Serviço Consolidado Disponível

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
            var mockDatabase = new Mock<IDatabase>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

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
            var mockDatabase = new Mock<IDatabase>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            ConfigurarHttpClientMockDisponivel();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.NotEqual(0, resultado);
            var consolidadoAtualizado = await _context.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataHoje);
            Assert.Equal(80, consolidadoAtualizado.TotalDebitos);
        }

        #endregion

        #region ValidaLancamento - Serviço Consolidado Indisponível

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
            var mockDatabase = new Mock<IDatabase>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            ConfigurarHttpClientMockIndisponivel();

            // Act
            var resultado = await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            Assert.NotEqual(0, resultado);
            var lancamentoRegistrado = await _context.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == lancamento.IdLancamento);
            Assert.NotNull(lancamentoRegistrado);
            Assert.Equal("Pendente", lancamentoRegistrado.Status);
            _mockRabbitPublisher.Verify(r => r.PublishLancamento(It.IsAny<Lancamento>()), Times.Once);
        }

        #endregion

        #region Exception Handling

        [Fact]
        public async Task RegistrarLancamentos_QuandoOcorreExcecao_DeveLogarERelancaExcecao()
        {
            // Arrange
            var lancamento = CriarLancamentoTeste();

            // Simular erro forçando uma exceção durante acesso ao banco
            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Throws(new Exception("Erro de conexão Redis"));

            // Act & Assert
            var excecao = await Assert.ThrowsAsync<Exception>(() => _commandStore.RegistrarLancamentos(lancamento));
            Assert.Contains("Erro de conexão Redis", excecao.Message);
        }

        #endregion

        #region Testes de Cache Redis

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
            var mockDatabase = new Mock<IDatabase>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var chaveEsperada = "lancamento:" + dataHoje.ToString("yyyy-MM-dd");
            mockDatabase.Verify(d => d.KeyDeleteAsync(chaveEsperada, It.IsAny<CommandFlags>()), Times.Once);
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
            var mockDatabase = new Mock<IDatabase>();

            _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);

            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            ConfigurarHttpClientMockDisponivel();

            // Act
            await _commandStore.RegistrarLancamentos(lancamento);

            // Assert
            var chaveEsperada = "consolidado:" + dataHoje.ToString("yyyy-MM-dd");
            mockDatabase.Verify(d => d.KeyDeleteAsync(chaveEsperada, It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion

        #region Helper Methods

        private static Lancamento CriarLancamentoTeste(
            char tipo = 'C',
            decimal valor = 100,
            DateTime? dataLancamento = null)
        {
            return new Lancamento
            {
                IdLancamento = new Random().Next(1, 10000),
                Tipo = tipo,
                Valor = valor,
                DataLancamento = dataLancamento ?? DateTime.Now,
                Status = "Pendente",
                Descricao = "Teste",
                DataConsolidacao = DateTime.Now.Date
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
    }
}
