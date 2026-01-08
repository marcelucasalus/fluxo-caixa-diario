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
using System.Diagnostics;
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
    private readonly LancamentoQueryStore _lancamentoQueryStore;

    public LancamentoQueryStoreTests()
    {
        _mockContext = new Mock<FluxoCaixaContext>(MockBehavior.Loose, 
            new DbContextOptions<FluxoCaixaContext>());
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<LancamentoQueryStore>>();

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

            var meter = new System.Diagnostics.Metrics.Meter("LancamentoQueryStore.Tests");
        
        _lancamentoQueryStore = new LancamentoQueryStore(
            _mockContext.Object, 
            _mockRedis.Object,
            _mockLogger.Object,
            meter);
    }

    #region Helper Methods

    private void SetupMockDbSet(List<Lancamento> lancamentos)
    {
        var queryable = lancamentos.AsQueryable();
        
        var mockDbSet = new Mock<DbSet<Lancamento>>();
        mockDbSet.As<IQueryable<Lancamento>>().Setup(m => m.Provider).Returns(queryable.Provider);
        mockDbSet.As<IQueryable<Lancamento>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockDbSet.As<IQueryable<Lancamento>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockDbSet.As<IQueryable<Lancamento>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        _mockContext
            .Setup(c => c.Lancamentos)
            .Returns(mockDbSet.Object);
    }

    #endregion

    #region ObterLancamento_RetornaDoRedis_Sucesso

    [Fact]
    public async Task ObterLancamento_QuandoDadosExistemNoRedis_DeveRetornarDoCache()
    {
        // Arrange
        var data = DateTime.Now;
        var chaveRedis = "lancamento:" + data.Date.ToString("yyyy-MM-dd");

        var lancamentos = new List<Lancamento>
        {
            new Lancamento
            {
                IdLancamento = 1,
                Descricao = "Venda",
                Valor = 100.00m,
                DataLancamento = data,
                Tipo = 'C'
            },
            new Lancamento
            {
                IdLancamento = 2,
                Descricao = "Despesa",
                Valor = 50.00m,
                DataLancamento = data,
                Tipo = 'D'
            }
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
        Assert.Equal("Venda", resultadoList[0].Descricao);
        Assert.Equal(100.00m, resultadoList[0].Valor);
        Assert.Equal(TipoLancamento.Credito, resultadoList[0].Tipo);
        Assert.Equal("Despesa", resultadoList[1].Descricao);
        Assert.Equal(50.00m, resultadoList[1].Valor);
        Assert.Equal(TipoLancamento.Debito, resultadoList[1].Tipo);

        _mockDatabase.Verify(
            db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    #endregion

    

    #region ObterLancamento_MapeamentoDados

    [Fact]
    public async Task ObterLancamento_DeveMapearCorretamenteLancamentoParaQueryResult()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var chaveRedis = "lancamento:2025-11-14";

        var lancamento = new Lancamento
        {
            IdLancamento = 123,
            Descricao = "Pagamento de Fornecedor",
            Valor = 1500.75m,
            DataLancamento = data,
            Tipo = 'D'
        };

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
    public async Task ObterLancamento_DeveConvertLancamentoTipoCParaCredito()
    {
        // Arrange
        var data = DateTime.Now;
        var chaveRedis = "lancamento:" + data.Date.ToString("yyyy-MM-dd");

        var lancamento = new Lancamento
        {
            IdLancamento = 1,
            Descricao = "Receita",
            Valor = 200.00m,
            DataLancamento = data,
            Tipo = 'C'
        };

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
        Assert.Equal(TipoLancamento.Credito, resultadoList[0].Tipo);
    }

    [Fact]
    public async Task ObterLancamento_DeveConvertLancamentoTipoDParaDebito()
    {
        // Arrange
        var data = DateTime.Now;
        var chaveRedis = "lancamento:" + data.Date.ToString("yyyy-MM-dd");

        var lancamento = new Lancamento
        {
            IdLancamento = 1,
            Descricao = "Despesa",
            Valor = 200.00m,
            DataLancamento = data,
            Tipo = 'D'
        };

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
        Assert.Equal(TipoLancamento.Debito, resultadoList[0].Tipo);
    }

    #endregion

    #region ObterLancamento_Comportamento

    [Fact]
    public async Task ObterLancamento_NaoDeveChamarStringSetQuandoCacheJaTemDados()
    {
        // Arrange
        var data = DateTime.Now;
        var chaveRedis = "lancamento:" + data.Date.ToString("yyyy-MM-dd");

        var lancamentos = new List<Lancamento>
        {
            new Lancamento
            {
                IdLancamento = 1,
                Descricao = "Venda",
                Valor = 100.00m,
                DataLancamento = data,
                Tipo = 'C'
            }
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
                It.IsAny<TimeSpan>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

   

    [Fact]
    public async Task ObterLancamento_DeveUsarChaveComFormatoCorreto()
    {
        // Arrange
        var data = new DateTime(2025, 11, 14);
        var expectedKey = "lancamento:2025-11-14";

        var lancamentos = new List<Lancamento>
        {
            new Lancamento
            {
                IdLancamento = 1,
                Descricao = "Venda",
                Valor = 100.00m,
                DataLancamento = data,
                Tipo = 'C'
            }
        };

        var json = JsonSerializer.Serialize(lancamentos);
        var redisValue = new RedisValue(json);

        _mockDatabase
            .Setup(db => db.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValue);

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockDatabase.Verify(
            db => db.StringGetAsync(expectedKey, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    #endregion

    
}
