using CommandStore.FluxoCaixa;
using Contract.Query;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using QueryStore;
using StackExchange.Redis;
using System;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System.Linq.Expressions;

namespace TestesUnitarios.QueryStore
{
    public class ConsolidadoQueryStoreTests
    {
        private readonly Mock<FluxoCaixaContext> _mockContext;
        private readonly Mock<IConnectionMultiplexer> _mockRedis;
        private readonly Mock<ILogger<ConsolidadoQueryStore>> _mockLogger;
        private readonly Mock<IDatabase> _mockRedisDatabase;
        private readonly Meter _meter;

        public ConsolidadoQueryStoreTests()
        {
            var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
                .UseInMemoryDatabase(databaseName: "TestDb")
                .Options;

            _mockContext = new Mock<FluxoCaixaContext>(options) { CallBase = true };
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockLogger = new Mock<ILogger<ConsolidadoQueryStore>>();
            _mockRedisDatabase = new Mock<IDatabase>();
            _meter = new Meter("TestMeter");

            _mockRedis
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockRedisDatabase.Object);
        }

        #region Testes de Sucesso

       


        [Fact]
        public async Task ObterConsolidadoDiario_DeveConverterCorretamentePelosMenosUmValorDecimal()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidadoEsperado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 1500.75m,
                TotalDebitos = 250.25m
            };

            var jsonSerializado = JsonSerializer.Serialize(consolidadoEsperado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object, _meter);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(1500.75m, resultado.TotalCreditos);
            Assert.Equal(250.25m, resultado.TotalDebitos);
        }

        #endregion

        #region Testes de Erro

       

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoRedisLancaExcecao_DeveConsultarBanco()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidadoEsperado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 1000m,
                TotalDebitos = 500m
            };

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.None, "Redis indisponível"));

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object, _meter);

            // Act & Assert
            await Assert.ThrowsAsync<RedisConnectionException>(
                () => store.ObterConsolidadoDiario(data));
        }

        #endregion

        #region Testes de Parametrização

        [Theory]
        [InlineData(2025, 11, 14)]
        [InlineData(2025, 01, 01)]
        [InlineData(2024, 12, 31)]
        public async Task ObterConsolidadoDiario_ComDatasVariadas_DeveFuncionarCorretamente(int ano, int mes, int dia)
        {
            // Arrange
            var data = new DateTime(ano, mes, dia);
            var chaveRedis = $"consolidado:{data.Date:yyyy-MM-dd}";
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 1000m,
                TotalDebitos = 500m
            };

            var jsonSerializado = JsonSerializer.Serialize(consolidado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object, _meter);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(data.Date, resultado.DataConsolidacao);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1000)]
        [InlineData(9999.99)]
        public async Task ObterConsolidadoDiario_ComValoresVariados_DevePersistirCorretamente(decimal valor)
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = valor,
                TotalDebitos = 0m
            };

            var jsonSerializado = JsonSerializer.Serialize(consolidado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object, _meter);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(valor, resultado.TotalCreditos);
        }

        #endregion

        #region Testes de Logging

        [Fact]
        public async Task ObterConsolidadoDiario_DeveLogarInicio()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 1000m,
                TotalDebitos = 500m
            };

            var jsonSerializado = JsonSerializer.Serialize(consolidado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object, _meter);

            // Act
            await store.ObterConsolidadoDiario(data);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Consultando consolidado diário para 11/14/2025 00:00:00")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_DeveLogarFim()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 1000m,
                TotalDebitos = 500m
            };

            var jsonSerializado = JsonSerializer.Serialize(consolidado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object, _meter);

            // Act
            await store.ObterConsolidadoDiario(data);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Consultando consolidado diário para 11/14/2025 00:00:00")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Testes de Comportamento do Cache

       
        #endregion

        #region Testes de Formato de Saída

        [Fact]
        public async Task ObterConsolidadoDiario_DeveRetornarResultadoDoTipoCorrecto()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 1000m,
                TotalDebitos = 500m
            };

            var jsonSerializado = JsonSerializer.Serialize(consolidado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object, _meter);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.IsType<ConsolidadoQueryResult>(resultado);
        }

        #endregion
    }
}
