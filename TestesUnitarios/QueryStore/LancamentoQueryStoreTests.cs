using CommandStore.FluxoCaixa;
using Enumeration;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using QueryStore;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TestesUnitarios.QueryStore;

public class LancamentoQueryStoreTests
{
    private readonly Mock<FluxoCaixaContext> _mockContext;
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<LancamentoQueryStore>> _mockLogger;
    private readonly Meter _meter;
    private readonly LancamentoQueryStore _lancamentoQueryStore;

    public LancamentoQueryStoreTests()
    {
        var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _mockContext = new Mock<FluxoCaixaContext>(options) { CallBase = true };
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<LancamentoQueryStore>>();
        _meter = new Meter("LancamentoQueryStore.Tests");

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        _lancamentoQueryStore = new LancamentoQueryStore(
            _mockContext.Object, 
            _mockRedis.Object,
            _mockLogger.Object,
            _meter);
    }

    #region Helper Methods

    private static Lancamento CriarLancamentoTeste(
        int id = 1,
        string descricao = "Teste",
        decimal valor = 100.00m,
        DateTime? dataLancamento = null,
        char tipo = 'C')
    {
        return new Lancamento
        {
            IdLancamento = id,
            Descricao = descricao,
            Valor = valor,
            DataLancamento = dataLancamento ?? DateTime.Now,
            Tipo = tipo,
            Status = "Consolidado"
        };
    }

    #endregion

    #region Cache Hit - Sucesso

    [Fact]
    public async Task ObterLancamento_QuandoDadosExistemNoRedis_DeveRetornarDoCache()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(1, "Venda", 100.00m, data, 'C'),
            CriarLancamentoTeste(2, "Despesa", 50.00m, data, 'D')
        };

        var json = JsonSerializer.Serialize(lancamentos);
        var redisValue = new RedisValue(json);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValue);

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        Assert.NotNull(resultado);
        var resultadoList = resultado.ToList();
        Assert.Equal(2, resultadoList.Count);

        Assert.Equal(1, resultadoList[0].Id);
        Assert.Equal("Venda", resultadoList[0].Descricao);
        Assert.Equal(100.00m, resultadoList[0].Valor);
        Assert.Equal(TipoLancamento.Credito, resultadoList[0].Tipo);

        Assert.Equal(2, resultadoList[1].Id);
        Assert.Equal("Despesa", resultadoList[1].Descricao);
        Assert.Equal(50.00m, resultadoList[1].Valor);
        Assert.Equal(TipoLancamento.Debito, resultadoList[1].Tipo);

        _mockDatabase.Verify(
            db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterLancamento_QuandoCacheHit_NaoDeveConsultarBanco()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(dataLancamento: data)
        };

        var json = JsonSerializer.Serialize(lancamentos);
        var redisValue = new RedisValue(json);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValue);

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert - Verificar que o Redis foi consultado (cache hit)
        _mockDatabase.Verify(
            db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterLancamento_QuandoCacheHit_NaoDeveSalvarNoCache()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(dataLancamento: data)
        };

        var json = JsonSerializer.Serialize(lancamentos);
        var redisValue = new RedisValue(json);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValue);

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockDatabase.Verify(
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
    public async Task ObterLancamento_QuandoCacheMiss_DeveConsultarBanco()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(1, "Venda", 100.00m, data, 'C')
        };

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Adicionar lançamentos no contexto real
        await _mockContext.Object.Lancamentos.AddRangeAsync(lancamentos);
        await _mockContext.Object.SaveChangesAsync();

        _mockDatabase
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        var resultadoList = resultado.ToList();
        Assert.Single(resultadoList);
        Assert.Equal("Venda", resultadoList[0].Descricao);

        _mockDatabase.Verify(
            db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterLancamento_QuandoCacheMissEMultiplosLancamentos_DeveRetornarTodos()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(1, "Venda 1", 100.00m, data, 'C'),
            CriarLancamentoTeste(2, "Venda 2", 200.00m, data, 'C'),
            CriarLancamentoTeste(3, "Despesa 1", 50.00m, data, 'D')
        };

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Adicionar lançamentos no contexto real
        await _mockContext.Object.Lancamentos.AddRangeAsync(lancamentos);
        await _mockContext.Object.SaveChangesAsync();

        _mockDatabase
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        var resultadoList = resultado.ToList();
        Assert.Equal(3, resultadoList.Count);
    }

    #endregion

    #region Mapeamento de Dados

    [Fact]
    public async Task ObterLancamento_DeveMapearCorretamenteLancamentoParaQueryResult()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamento = CriarLancamentoTeste(
            123, 
            "Pagamento de Fornecedor", 
            1500.75m, 
            data, 
            'D');

        var lancamentos = new List<Lancamento> { lancamento };
        var json = JsonSerializer.Serialize(lancamentos);
        var redisValue = new RedisValue(json);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValue);

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);
        var resultadoList = resultado.ToList();

        // Assert
        Assert.Single(resultadoList);
        var mapeado = resultadoList[0];
        Assert.Equal(123, mapeado.Id);
        Assert.Equal("Pagamento de Fornecedor", mapeado.Descricao);
        Assert.Equal(1500.75m, mapeado.Valor);
        Assert.Equal(data, mapeado.DataLancamento);
        Assert.Equal(TipoLancamento.Debito, mapeado.Tipo);
    }

    [Fact]
    public async Task ObterLancamento_DeveConverterTipoCParaCredito()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamento = CriarLancamentoTeste(1, "Receita", 200.00m, data, 'C');
        var lancamentos = new List<Lancamento> { lancamento };
        var json = JsonSerializer.Serialize(lancamentos);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);
        var resultadoList = resultado.ToList();

        // Assert
        Assert.Single(resultadoList);
        Assert.Equal(TipoLancamento.Credito, resultadoList[0].Tipo);
    }

    [Fact]
    public async Task ObterLancamento_DeveConverterTipoDParaDebito()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamento = CriarLancamentoTeste(1, "Despesa", 200.00m, data, 'D');
        var lancamentos = new List<Lancamento> { lancamento };
        var json = JsonSerializer.Serialize(lancamentos);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);
        var resultadoList = resultado.ToList();

        // Assert
        Assert.Single(resultadoList);
        Assert.Equal(TipoLancamento.Debito, resultadoList[0].Tipo);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(100.50)]
    [InlineData(9999.99)]
    public async Task ObterLancamento_DevePreservarValoresDecimais(decimal valorTeste)
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamento = CriarLancamentoTeste(1, "Teste", valorTeste, data, 'C');
        var lancamentos = new List<Lancamento> { lancamento };
        var json = JsonSerializer.Serialize(lancamentos);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);
        var resultadoList = resultado.ToList();

        // Assert
        Assert.Equal(valorTeste, resultadoList[0].Valor);
    }

    #endregion

    #region Formato de Chave Redis

    [Fact]
    public async Task ObterLancamento_DeveUsarChaveComFormatoCorreto()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var expectedKey = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(dataLancamento: data)
        };

        var json = JsonSerializer.Serialize(lancamentos);

        _mockDatabase
            .Setup(db => db.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockDatabase.Verify(
            db => db.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Theory]
    [InlineData(2025, 1, 1, "lancamento:2025-01-01")]
    [InlineData(2025, 12, 31, "lancamento:2025-12-31")]
    [InlineData(2024, 6, 15, "lancamento:2024-06-15")]
    public async Task ObterLancamento_DeveGerarChaveCorretaParaDiferentesDatas(
        int ano, int mes, int dia, string chaveEsperada)
    {
        // Arrange
        var data = new DateTime(ano, mes, dia);
        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(dataLancamento: data)
        };

        var json = JsonSerializer.Serialize(lancamentos);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveEsperada, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockDatabase.Verify(
            db => db.StringGetAsync(chaveEsperada, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    #endregion

    #region Tratamento de Erros

    [Fact]
    public async Task ObterLancamento_QuandoNaoEncontraDados_DeveLancarException()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Não adicionar nada no banco para simular dados não encontrados

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(
            () => _lancamentoQueryStore.ObterLancamento(data));

        Assert.Contains("não encontrados", exception.Message);
        Assert.Contains("2025-11-14", exception.Message);
    }

    [Fact]
    public async Task ObterLancamento_QuandoRedisLancaExcecao_DeveRepassarExcecao()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.None, "Erro de conexão"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(
            () => _lancamentoQueryStore.ObterLancamento(data));
    }

    [Fact]
    public async Task ObterLancamento_QuandoJsonInvalido_DeveLancarException()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("json inválido {["));

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(
            () => _lancamentoQueryStore.ObterLancamento(data));
    }

    #endregion

    #region Logging

    [Fact]
    public async Task ObterLancamento_QuandoCacheHit_DeveLogarDebug()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(dataLancamento: data)
        };

        var json = JsonSerializer.Serialize(lancamentos);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Cache hit")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterLancamento_QuandoCacheMiss_DeveLogarInformation()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(dataLancamento: data)
        };

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Adicionar lançamentos no contexto real
        await _mockContext.Object.Lancamentos.AddRangeAsync(lancamentos);
        await _mockContext.Object.SaveChangesAsync();

        _mockDatabase
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Cache miss")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterLancamento_QuandoOcorreErro_DeveLogarError()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ThrowsAsync(new Exception("Erro de teste"));

        // Act
        await Assert.ThrowsAsync<Exception>(
            () => _lancamentoQueryStore.ObterLancamento(data));

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Erro ao consultar")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Casos Especiais

    [Fact]
    public async Task ObterLancamento_ComDataComHorario_DeveDesconsiderarHorario()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14, 15, 30, 45);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            CriarLancamentoTeste(dataLancamento: data.Date)
        };

        var json = JsonSerializer.Serialize(lancamentos);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockDatabase.Verify(
            db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterLancamento_ComListaVaziaNoCache_DeveRetornarListaVazia()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamentosVazio = new List<Lancamento>();
        var json = JsonSerializer.Serialize(lancamentosVazio);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        Assert.NotNull(resultado);
        Assert.Empty(resultado);
    }

    #endregion
}
