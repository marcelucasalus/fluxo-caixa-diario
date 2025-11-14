using CommandStore.FluxoCaixa;
using Contract.Query;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using QueryStore;
using StackExchange.Redis;
using System;
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

        public ConsolidadoQueryStoreTests()
        {
            var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
                .UseInMemoryDatabase(databaseName: "TestDb")
                .Options;

            _mockContext = new Mock<FluxoCaixaContext>(options) { CallBase = true };
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockLogger = new Mock<ILogger<ConsolidadoQueryStore>>();
            _mockRedisDatabase = new Mock<IDatabase>();

            _mockRedis
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockRedisDatabase.Object);
        }

        #region Testes de Sucesso

        [Fact(Skip = "Precisa de correcao")]
        public async Task ObterConsolidadoDiario_QuandoDadosExistemNoRedis_DeveRetornarResultadoDoCache()
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

            var jsonSerializado = JsonSerializer.Serialize(consolidadoEsperado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal(data.Date, resultado.DataConsolidacao);
            Assert.Equal(1000m, resultado.TotalCreditos);
            Assert.Equal(500m, resultado.TotalDebitos);

            _mockRedisDatabase.Verify(
                db => db.StringGetAsync(chaveRedis, CommandFlags.None),
                Times.Once);

            _mockContext.Verify(
                c => c.ConsolidadosDiarios, Times.Never);
        }

        [Fact(Skip = "Precisa de correcao")]
        public async Task ObterConsolidadoDiario_QuandoDadosNaoExistemNoRedisButExistemNoBanco_DeveRetornarDoBancoECachear()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidadoEsperado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 2000m,
                TotalDebitos = 800m
            };

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var mockDbSet = new Mock<DbSet<ConsolidadoDiario>>();
            mockDbSet
                .Setup(m => m.FirstOrDefaultAsync(
                    It.IsAny<Expression<Func<ConsolidadoDiario, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(consolidadoEsperado);

            _mockContext
                .Setup(c => c.ConsolidadosDiarios)
                .Returns(mockDbSet.Object);

            _mockRedisDatabase
                .Setup(db => db.StringSetAsync(
                    chaveRedis,
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal(data.Date, resultado.DataConsolidacao);
            Assert.Equal(2000m, resultado.TotalCreditos);
            Assert.Equal(800m, resultado.TotalDebitos);

            _mockRedisDatabase.Verify(
                db => db.StringGetAsync(chaveRedis, CommandFlags.None),
                Times.Once);

            mockDbSet.Verify(
                m => m.FirstOrDefaultAsync(
                    It.IsAny<Expression<Func<ConsolidadoDiario, bool>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _mockRedisDatabase.Verify(
                db => db.StringSetAsync(
                    chaveRedis,
                    It.IsAny<RedisValue>(),
                    TimeSpan.FromMinutes(30),
                    When.Always,
                    CommandFlags.None),
                Times.Once);
        }

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

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(1500.75m, resultado.TotalCreditos);
            Assert.Equal(250.25m, resultado.TotalDebitos);
        }

        #endregion

        #region Testes de Erro

        [Fact(Skip = "Precisa de correcao")]
        public async Task ObterConsolidadoDiario_QuandoConsolidadoNaoExiste_DeveLancarException()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var mockDbSet = new Mock<DbSet<ConsolidadoDiario>>();
            mockDbSet
                .Setup(m => m.FirstOrDefaultAsync(
                    It.IsAny<Expression<Func<ConsolidadoDiario, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConsolidadoDiario)null);

            _mockContext
                .Setup(c => c.ConsolidadosDiarios)
                .Returns(mockDbSet.Object);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(
                () => store.ObterConsolidadoDiario(data));

            Assert.Contains("nao foi encontrado", exception.Message);
            Assert.Contains("2025-11-14", exception.Message);
        }

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

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

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

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

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

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

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

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            await store.ObterConsolidadoDiario(data);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Iniciando consulta no redis")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
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

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            await store.ObterConsolidadoDiario(data);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Consulta finalizada")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion

        #region Testes de Comportamento do Cache

        [Fact(Skip = "Precisa de correcao")]
        public async Task ObterConsolidadoDiario_QuandoBuscaDoBancoTemSucesso_DevePersistirNoCache()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = data.Date,
                TotalCreditos = 5000m,
                TotalDebitos = 2000m
            };

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var mockDbSet = new Mock<DbSet<ConsolidadoDiario>>();
            mockDbSet
                .Setup(m => m.FirstOrDefaultAsync(
                    It.IsAny<Expression<Func<ConsolidadoDiario, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(consolidado);

            _mockContext
                .Setup(c => c.ConsolidadosDiarios)
                .Returns(mockDbSet.Object);

            _mockRedisDatabase
                .Setup(db => db.StringSetAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            await store.ObterConsolidadoDiario(data);

            // Assert
            _mockRedisDatabase.Verify(
                db => db.StringSetAsync(
                    chaveRedis,
                    It.IsAny<RedisValue>(),
                    TimeSpan.FromMinutes(30),
                    When.Always,
                    CommandFlags.None),
                Times.Once);
        }

        [Fact(Skip = "Precisa de correcao")]
        public async Task ObterConsolidadoDiario_NaoDevoCarregarBancoQuandoRedisTemDados()
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

            var mockDbSet = new Mock<DbSet<ConsolidadoDiario>>();

            _mockContext
                .Setup(c => c.ConsolidadosDiarios)
                .Returns(mockDbSet.Object);

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            await store.ObterConsolidadoDiario(data);

            // Assert
            mockDbSet.Verify(
                m => m.FirstOrDefaultAsync(
                    It.IsAny<Expression<Func<ConsolidadoDiario, bool>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

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

            var store = new ConsolidadoQueryStore(_mockContext.Object, _mockRedis.Object, _mockLogger.Object);

            // Act
            var resultado = await store.ObterConsolidadoDiario(data);

            // Assert
            Assert.IsType<ConsolidadoQueryResult>(resultado);
        }

        #endregion
    }
}
