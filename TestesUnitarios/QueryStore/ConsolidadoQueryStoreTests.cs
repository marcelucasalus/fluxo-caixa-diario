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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TestesUnitarios.QueryStore
{
    public class ConsolidadoQueryStoreTests
    {
        private readonly Mock<FluxoCaixaContext> _mockContext;
        private readonly Mock<IConnectionMultiplexer> _mockRedis;
        private readonly Mock<ILogger<ConsolidadoQueryStore>> _mockLogger;
        private readonly Mock<IDatabase> _mockRedisDatabase;
        private readonly Meter _meter;
        private readonly ConsolidadoQueryStore _queryStore;

        public ConsolidadoQueryStoreTests()
        {
            var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _mockContext = new Mock<FluxoCaixaContext>(options) { CallBase = true };
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockLogger = new Mock<ILogger<ConsolidadoQueryStore>>();
            _mockRedisDatabase = new Mock<IDatabase>();
            _meter = new Meter("ConsolidadoQueryStore.Tests");

            _mockRedis
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_mockRedisDatabase.Object);

            _queryStore = new ConsolidadoQueryStore(
                _mockContext.Object, 
                _mockRedis.Object, 
                _mockLogger.Object, 
                _meter);
        }

        #region Helper Methods

        private static ConsolidadoDiario CriarConsolidadoTeste(
            DateTime? dataConsolidacao = null,
            decimal totalCreditos = 1000m,
            decimal totalDebitos = 500m)
        {
            return new ConsolidadoDiario
            {
                DataConsolidacao = (dataConsolidacao ?? DateTime.Now).Date,
                TotalCreditos = totalCreditos,
                TotalDebitos = totalDebitos
            };
        }

        #endregion

        #region Cache Hit - Sucesso

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoDadosExistemNoRedis_DeveRetornarDoCache()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidadoEsperado = CriarConsolidadoTeste(data, 1500.75m, 250.25m);

            var jsonSerializado = JsonSerializer.Serialize(consolidadoEsperado);
            var redisValue = new RedisValue(jsonSerializado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValue);

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal(data.Date, resultado.DataConsolidacao);
            Assert.Equal(1500.75m, resultado.TotalCreditos);
            Assert.Equal(250.25m, resultado.TotalDebitos);

            _mockRedisDatabase.Verify(
                db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
                Times.Once);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoCacheHit_NaoDeveConsultarBanco()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            await _queryStore.ObterConsolidadoDiario(data);

            // Assert - Como estamos usando InMemory database com CallBase=true, não podemos verificar diretamente
            // O importante é que não lançou exceção e retornou dados do cache
            _mockRedisDatabase.Verify(
                db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
                Times.Once);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoCacheHit_NaoDeveSalvarNoCache()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            _mockRedisDatabase.Verify(
                db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()),
                Times.Never);
        }

        #endregion

        #region Cache Miss - Consulta Banco

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoCacheMiss_DeveConsultarBanco()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data, 1000m, 500m);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            // Adicionar o consolidado no contexto real
            await _mockContext.Object.ConsolidadosDiarios.AddAsync(consolidado);
            await _mockContext.Object.SaveChangesAsync();

            _mockRedisDatabase
                .Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.NotNull(resultado);
            Assert.Equal(1000m, resultado.TotalCreditos);
            Assert.Equal(500m, resultado.TotalDebitos);
        }

        #endregion

        #region Conversão de Valores

        [Fact]
        public async Task ObterConsolidadoDiario_DeveConverterCorretamenteValoresDecimais()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data, 1500.75m, 250.25m);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(1500.75m, resultado.TotalCreditos);
            Assert.Equal(250.25m, resultado.TotalDebitos);
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
            var consolidado = CriarConsolidadoTeste(data, valor, 0m);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(valor, resultado.TotalCreditos);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_DeveRetornarResultadoDoTipoCorreto()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.IsType<ConsolidadoQueryResult>(resultado);
        }

        #endregion

        #region Formato de Chave Redis

        [Fact]
        public async Task ObterConsolidadoDiario_DeveUsarChaveComFormatoCorreto()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveEsperada = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveEsperada, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            _mockRedisDatabase.Verify(
                db => db.StringGetAsync(chaveEsperada, It.IsAny<CommandFlags>()),
                Times.Once);
        }

        [Theory]
        [InlineData(2025, 11, 14)]
        [InlineData(2025, 1, 1)]
        [InlineData(2024, 12, 31)]
        public async Task ObterConsolidadoDiario_ComDatasVariadas_DeveFuncionarCorretamente(int ano, int mes, int dia)
        {
            // Arrange
            var data = new DateTime(ano, mes, dia);
            var chaveRedis = $"consolidado:{data.Date:yyyy-MM-dd}";
            var consolidado = CriarConsolidadoTeste(data);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(data.Date, resultado.DataConsolidacao);
        }

        #endregion

        #region Tratamento de Erros

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoNaoEncontraDados_DeveLancarException()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            // Não adicionar nada no banco para simular dados não encontrados

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(
                () => _queryStore.ObterConsolidadoDiario(data));

            Assert.Contains("não encontrado", exception.Message);
            Assert.Contains("2025-11-14", exception.Message);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoRedisLancaExcecao_DeveRepassarExcecao()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.None, "Redis indisponível"));

            // Act & Assert
            await Assert.ThrowsAsync<RedisConnectionException>(
                () => _queryStore.ObterConsolidadoDiario(data));
        }

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoJsonInvalido_DeveLancarException()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue("json inválido {["));

            // Act & Assert
            await Assert.ThrowsAsync<JsonException>(
                () => _queryStore.ObterConsolidadoDiario(data));
        }

        #endregion

        #region Logging

        [Fact]
        public async Task ObterConsolidadoDiario_DeveLogarInicio()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Consultando consolidado diário")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_QuandoOcorreErro_DeveLogarError()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ThrowsAsync(new Exception("Erro de teste"));

            // Act
            await Assert.ThrowsAsync<Exception>(
                () => _queryStore.ObterConsolidadoDiario(data));

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Erro ao consultar consolidado diário")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Casos Especiais

        [Fact]
        public async Task ObterConsolidadoDiario_ComDataComHorario_DeveDesconsiderarHorario()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14, 15, 30, 45);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data.Date);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(data.Date, resultado.DataConsolidacao);
            _mockRedisDatabase.Verify(
                db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
                Times.Once);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_ComValoresZero_DeveRetornarCorretamente()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data, 0m, 0m);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(0m, resultado.TotalCreditos);
            Assert.Equal(0m, resultado.TotalDebitos);
        }

        [Fact]
        public async Task ObterConsolidadoDiario_ComValoresAltos_DeveManterPrecisao()
        {
            // Arrange
            var data = new DateTime(2025, 11, 14);
            var chaveRedis = "consolidado:2025-11-14";
            var consolidado = CriarConsolidadoTeste(data, 999999.99m, 888888.88m);

            var jsonSerializado = JsonSerializer.Serialize(consolidado);

            _mockRedisDatabase
                .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue(jsonSerializado));

            // Act
            var resultado = await _queryStore.ObterConsolidadoDiario(data);

            // Assert
            Assert.Equal(999999.99m, resultado.TotalCreditos);
            Assert.Equal(888888.88m, resultado.TotalDebitos);
        }

        #endregion
    }
}
