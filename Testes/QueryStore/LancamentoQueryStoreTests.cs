using CommandStore.FluxoCaixa;
using Contract.Query;
using Enumeration;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Moq;
using QueryStore;
using StackExchange.Redis;
using System.Text.Json;
using Xunit;

namespace Testes.QueryStore;

public class LancamentoQueryStoreTests
{
    private readonly Mock<FluxoCaixaContext> _mockContext;
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly LancamentoQueryStore _lancamentoQueryStore;

    public LancamentoQueryStoreTests()
    {
        _mockContext = new Mock<FluxoCaixaContext>();
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        _lancamentoQueryStore = new LancamentoQueryStore(_mockContext.Object, _mockRedis.Object);
    }

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

    #region ObterLancamento_RetornaDoDatabase_Sucesso

    [Fact]
    public async Task ObterLancamento_QuandoCacheEstaVazio_DeveRetornarDoBancoDados()
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
        }.AsQueryable();

        var mockDbSet = new Mock<DbSet<Lancamento>>();
        mockDbSet
            .Setup(m => m.Where(It.IsAny<Func<Lancamento, bool>>()))
            .Returns((Func<Lancamento, bool> predicate) =>
                lancamentos.Where(predicate).AsQueryable());

        _mockContext
            .Setup(c => c.Lancamentos)
            .Returns(mockDbSet.Object);

        var redisValueEmpty = new RedisValue();
        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValueEmpty);

        _mockDatabase
            .Setup(db => db.StringSetAsync(
                chaveRedis,
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        Assert.NotNull(resultado);
        var resultadoList = resultado.ToList();
        Assert.Single(resultadoList);
        Assert.Equal("Venda", resultadoList[0].Descricao);
        Assert.Equal(100.00m, resultadoList[0].Valor);
        Assert.Equal(TipoLancamento.Credito, resultadoList[0].Tipo);

        _mockDatabase.Verify(
            db => db.StringSetAsync(
                chaveRedis,
                It.IsAny<RedisValue>(),
                TimeSpan.FromMinutes(30),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    #endregion

    #region ObterLancamento_Erro

    [Fact]
    public async Task ObterLancamento_QuandoNaoEncontraDados_DeveLancarExcecao()
    {
        // Arrange
        var data = DateTime.Now;
        var chaveRedis = "lancamento:" + data.Date.ToString("yyyy-MM-dd");

        var lancamentosVazios = new List<Lancamento>().AsQueryable();

        var mockDbSet = new Mock<DbSet<Lancamento>>();
        mockDbSet
            .Setup(m => m.Where(It.IsAny<Func<Lancamento, bool>>()))
            .Returns((Func<Lancamento, bool> predicate) =>
                lancamentosVazios.Where(predicate).AsQueryable());

        _mockContext
            .Setup(c => c.Lancamentos)
            .Returns(mockDbSet.Object);

        var redisValueEmpty = new RedisValue();
        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValueEmpty);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(() =>
            _lancamentoQueryStore.ObterLancamento(data));

        Assert.Contains($"nao foi encontrado", exception.Message);
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
    public async Task ObterLancamento_DeveCacheComExpiracao30Minutos()
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
        }.AsQueryable();

        var mockDbSet = new Mock<DbSet<Lancamento>>();
        mockDbSet
            .Setup(m => m.Where(It.IsAny<Func<Lancamento, bool>>()))
            .Returns((Func<Lancamento, bool> predicate) =>
                lancamentos.Where(predicate).AsQueryable());

        _mockContext
            .Setup(c => c.Lancamentos)
            .Returns(mockDbSet.Object);

        var redisValueEmpty = new RedisValue();
        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValueEmpty);

        _mockDatabase
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _lancamentoQueryStore.ObterLancamento(data);

        // Assert
        _mockDatabase.Verify(
            db => db.StringSetAsync(
                chaveRedis,
                It.IsAny<RedisValue>(),
                TimeSpan.FromMinutes(30),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
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

    #region ObterLancamento_MultiplasLinhas

    [Fact]
    public async Task ObterLancamento_DeveProcesarMultiplosProdutosEmParalelo()
    {
        // Arrange
        var data = DateTime.Now;
        var chaveRedis = "lancamento:" + data.Date.ToString("yyyy-MM-dd");

        var lancamentos = new List<Lancamento>();
        for (int i = 1; i <= 100; i++)
        {
            lancamentos.Add(new Lancamento
            {
                IdLancamento = i,
                Descricao = $"Lançamento {i}",
                Valor = (decimal)(i * 10),
                DataLancamento = data,
                Tipo = i % 2 == 0 ? 'C' : 'D'
            });
        }

        var json = JsonSerializer.Serialize(lancamentos);
        var redisValue = new RedisValue(json);

        _mockDatabase
            .Setup(db => db.StringGetAsync(chaveRedis, It.IsAny<CommandFlags>()))
            .ReturnsAsync(redisValue);

        // Act
        var resultado = await _lancamentoQueryStore.ObterLancamento(data);
        var resultadoList = resultado.ToList();

        // Assert
        Assert.Equal(100, resultadoList.Count);
        for (int i = 1; i <= 100; i++)
        {
            Assert.Contains(resultadoList, x => x.Id == i && x.Descricao == $"Lançamento {i}");
        }
    }

    #endregion
}