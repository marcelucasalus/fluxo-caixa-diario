using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommandStore.FluxoCaixa;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using QueryStore;
using StackExchange.Redis;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xunit;

namespace TestesUnitarios.Integration;

/// <summary>
/// Testes de integração que validam o fluxo completo do sistema
/// desde o registro de lançamentos até a consulta consolidada
/// </summary>
public class FluxoCaixaIntegrationTests : IDisposable
{
    private readonly FluxoCaixaContext _context;
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<IRabbitMqPublisher> _mockRabbitPublisher;
    private readonly Mock<ILogger<FluxoCaixaCommandStore>> _mockCommandLogger;
    private readonly Mock<ILogger<LancamentoQueryStore>> _mockLancamentoLogger;
    private readonly Mock<ILogger<ConsolidadoQueryStore>> _mockConsolidadoLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Meter _meter;
    private readonly ActivitySource _activitySource;
    private readonly FluxoCaixaCommandStore _commandStore;
    private readonly LancamentoQueryStore _lancamentoQueryStore;
    private readonly ConsolidadoQueryStore _consolidadoQueryStore;
    private static int _lancamentoId = 1;

    public FluxoCaixaIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FluxoCaixaContext(options);
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockRabbitPublisher = new Mock<IRabbitMqPublisher>();
        _mockCommandLogger = new Mock<ILogger<FluxoCaixaCommandStore>>();
        _mockLancamentoLogger = new Mock<ILogger<LancamentoQueryStore>>();
        _mockConsolidadoLogger = new Mock<ILogger<ConsolidadoQueryStore>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _meter = new Meter("FluxoCaixa.Integration.Tests");
        _activitySource = new ActivitySource("FluxoCaixa.Integration.Tests");

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        // Configurar Redis para sempre retornar null (cache miss) nos testes de integração
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _commandStore = new FluxoCaixaCommandStore(
            _context,
            _mockRedis.Object,
            _mockRabbitPublisher.Object,
            _mockCommandLogger.Object,
            _httpClient,
            _activitySource,
            _meter);

        _lancamentoQueryStore = new LancamentoQueryStore(
            _context,
            _mockRedis.Object,
            _mockLancamentoLogger.Object,
            _meter);

        _consolidadoQueryStore = new ConsolidadoQueryStore(
            _context,
            _mockRedis.Object,
            _mockConsolidadoLogger.Object,
            _meter);
    }

    private static Lancamento CriarLancamento(
        char tipo = 'C',
        decimal valor = 100m,
        DateTime? data = null,
        string descricao = "Teste Integração")
    {
        return new Lancamento
        {
            IdLancamento = _lancamentoId++,
            Tipo = tipo,
            Valor = valor,
            DataLancamento = data ?? DateTime.Now.Date,
            Status = "Pendente",
            Descricao = descricao,
            DataConsolidacao = (data ?? DateTime.Now).Date
        };
    }

    private void ConfigurarServicoDisponivel()
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

    private void ConfigurarServicoIndisponivel()
    {
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Serviço indisponível"));
    }

    #region Fluxo Completo - Registro e Consulta

    [Fact]
    public async Task FluxoCompleto_RegistrarEConsultarLancamentos_DeveRetornarDadosCorretos()
    {
        // Arrange
        var data = new DateTime(2025, 6, 15);
        ConfigurarServicoDisponivel();

        var lancamento1 = CriarLancamento('C', 500m, data, "Venda 1");
        var lancamento2 = CriarLancamento('C', 300m, data, "Venda 2");
        var lancamento3 = CriarLancamento('D', 150m, data, "Despesa");

        // Act - Registrar lançamentos
        await _commandStore.RegistrarLancamentos(lancamento1);
        await _commandStore.RegistrarLancamentos(lancamento2);
        await _commandStore.RegistrarLancamentos(lancamento3);

        // Act - Consultar lançamentos
        var lancamentos = await _lancamentoQueryStore.ObterLancamento(data);
        var lancamentosList = lancamentos.ToList();

        // Assert
        Assert.Equal(3, lancamentosList.Count);
        Assert.Contains(lancamentosList, l => l.Descricao == "Venda 1" && l.Valor == 500m);
        Assert.Contains(lancamentosList, l => l.Descricao == "Venda 2" && l.Valor == 300m);
        Assert.Contains(lancamentosList, l => l.Descricao == "Despesa" && l.Valor == 150m);
    }

    #endregion

    #region Serviço Indisponível

    [Fact]
    public async Task ServicoIndisponivel_RegistrarLancamentos_DeveMarcarComoPendente()
    {
        // Arrange
        var data = new DateTime(2025, 9, 5);
        ConfigurarServicoIndisponivel();

        var lancamento1 = CriarLancamento('C', 500m, data, "Venda Pendente");
        var lancamento2 = CriarLancamento('D', 200m, data, "Despesa Pendente");

        // Act
        await _commandStore.RegistrarLancamentos(lancamento1);
        await _commandStore.RegistrarLancamentos(lancamento2);

        // Assert - Verificar que foram publicados na fila
        _mockRabbitPublisher.Verify(
            r => r.PublishLancamento(It.IsAny<Lancamento>()), 
            Times.Exactly(2));

        // Assert - Verificar status no banco
        var lancamentos = await _context.Lancamentos
            .Where(l => l.DataLancamento.Date == data.Date)
            .ToListAsync();

        Assert.All(lancamentos, l => Assert.Equal("Pendente", l.Status));

        // Assert - Verificar que o consolidado não foi atualizado
        var consolidado = await _context.ConsolidadosDiarios
            .FirstOrDefaultAsync(c => c.DataConsolidacao == data.Date);

        Assert.NotNull(consolidado);
        Assert.Equal(0, consolidado.TotalCreditos);
        Assert.Equal(0, consolidado.TotalDebitos);
    }

    #endregion

    #region Cenários de Erro

    [Fact]
    public async Task ConsultarLancamentosInexistentes_DeveLancarException()
    {
        // Arrange
        var dataInexistente = new DateTime(2025, 12, 31);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            () => _lancamentoQueryStore.ObterLancamento(dataInexistente));
    }

    [Fact]
    public async Task ConsultarConsolidadoInexistente_DeveLancarException()
    {
        // Arrange
        var dataInexistente = new DateTime(2025, 12, 31);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            () => _consolidadoQueryStore.ObterConsolidadoDiario(dataInexistente));
    }

    #endregion

    #region Cleanup

    public void Dispose()
    {
        _context?.Dispose();
        _httpClient?.Dispose();
        _meter?.Dispose();
        _activitySource?.Dispose();
    }

    #endregion
}
