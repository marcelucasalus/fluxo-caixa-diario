using FluxoCaixa.LancamentoRegistrar.Entity;
using FluxoCaixa.LancamentoRegistrar.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CommandStore.FluxoCaixa
{
    public class FluxoCaixaCommandStore : IFluxoCaixaCommandStore
    {
        private readonly FluxoCaixaContext _context;
        private readonly IConnectionMultiplexer _redis;
        private readonly IRabbitMqPublisher _rabbitPublisher;
        private readonly ILogger<FluxoCaixaCommandStore> _logger;
        private readonly HttpClient _httpClient;
        private readonly ActivitySource _activity;

        // Métricas
        private readonly Counter<int> _lancamentosCreatedCounter;
        private readonly Histogram<double> _lancamentosCreationLatency;
        private readonly Counter<int> _lancamentosPendentesCounter;
        private readonly Counter<int> _lancamentosConsolidadosCounter;
        private readonly Counter<int> _consolidadoIndisponivelCounter;

        public FluxoCaixaCommandStore(
            FluxoCaixaContext context,
            IConnectionMultiplexer redis,
            IRabbitMqPublisher rabbitPublisher,
            ILogger<FluxoCaixaCommandStore> logger,
            HttpClient httpClient,
            ActivitySource activity,
            Meter meter)
        {
            _context = context;
            _redis = redis;
            _rabbitPublisher = rabbitPublisher;
            _logger = logger;
            _httpClient = httpClient;
            _activity = activity;

            // Criar métricas customizadas
            _lancamentosCreatedCounter = meter.CreateCounter<int>(
                "lancamentos.register.total",
                unit: "",
                description: "Total de lançamentos criados");

            _lancamentosCreationLatency = meter.CreateHistogram<double>(
                "lancamentos.register.duration",
                unit: "ms",
                description: "Duração do registro de lançamentos");

            _lancamentosPendentesCounter = meter.CreateCounter<int>(
                "lancamentos.pendentes.total",
                unit: "",
                description: "Total de lançamentos pendentes");

            _lancamentosConsolidadosCounter = meter.CreateCounter<int>(
                "lancamentos.consolidados.total",
                unit: "",
                description: "Total de lançamentos consolidados");

            _consolidadoIndisponivelCounter = meter.CreateCounter<int>(
                "consolidado.indisponivel.total",
                unit: "",
                description: "Total de falhas ao acessar o serviço de consolidado");
        }

        public async Task<int> RegistrarLancamentos(Lancamento lancamento)
        {
            using var activity = _activity.StartActivity(
                "FluxoCaixaCommandStore.RegistrarLancamentos",
                ActivityKind.Internal);

            activity?.SetTag("lancamento.data", lancamento.DataLancamento);
            activity?.SetTag("lancamento.valor", lancamento.Valor);
            activity?.SetTag("lancamento.tipo", lancamento.Tipo.ToString());

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var db = _redis.GetDatabase();

                using var dbActivity = _activity.StartActivity("BuscarConsolidadoDiario");
                var consolidado = await _context.ConsolidadosDiarios
                    .FirstOrDefaultAsync(c => c.DataConsolidacao.Date == lancamento.DataLancamento.Date);

                if (consolidado == null)
                {
                    activity?.AddEvent(new ActivityEvent("ConsolidadoNaoEncontrado"));

                    consolidado = new ConsolidadoDiario
                    {
                        DataConsolidacao = lancamento.DataLancamento.Date,
                        TotalCreditos = 0,
                        TotalDebitos = 0
                    };

                    await _context.ConsolidadosDiarios.AddAsync(consolidado);
                }

                var result = await ValidaLancamento(lancamento, consolidado, db);

                // Registrar métricas com tags
                var tags = new TagList
                {
                    { "tipo", lancamento.Tipo.ToString() },
                    { "status", lancamento.Status }
                };

                _lancamentosCreatedCounter.Add(1, tags);

                stopwatch.Stop();
                _lancamentosCreationLatency.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

                return result;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.RecordException(ex);
                _logger.LogError(ex, "Erro ao registrar lançamento");

                // Registrar métrica de erro
                var errorTags = new TagList { { "error", ex.GetType().Name } };
                _lancamentosCreatedCounter.Add(0, errorTags);

                throw;
            }
        }

        private async Task<int> ValidaLancamento(Lancamento lancamento, ConsolidadoDiario consolidado, IDatabase db)
        {
            using var activity = _activity.StartActivity(
                "FluxoCaixaCommandStore.ValidaLancamento",
                ActivityKind.Internal);

            activity?.SetTag("lancamento.id", lancamento.IdLancamento);

            await _context.Lancamentos.AddAsync(lancamento);

            using (_activity.StartActivity("Redis.KeyDelete.Lancamento"))
            {
                await db.KeyDeleteAsync("lancamento:" + lancamento.DataLancamento.ToString("yyyy-MM-dd"));
            }

            var consolidadoDisponivel = await ServiceConsolidadoDisponivel();
            activity?.SetTag("consolidado.disponivel", consolidadoDisponivel);

            if (!consolidadoDisponivel)
            {
                var tags = new TagList { { "tipo", lancamento.Tipo.ToString() } };
                _lancamentosPendentesCounter.Add(1, tags);

                activity?.AddEvent(new ActivityEvent("ServicoConsolidadoIndisponivel"));

                lancamento.Status = "Pendente";
                await _context.SaveChangesAsync();

                using (_activity.StartActivity("RabbitMQ.PublishLancamento"))
                {
                    _rabbitPublisher.PublishLancamento(lancamento);
                }
            }
            else
            {
                var tags = new TagList { { "tipo", lancamento.Tipo.ToString() } };
                _lancamentosConsolidadosCounter.Add(1, tags);

                using var consolidarActivity = _activity.StartActivity("ConsolidarLancamento");

                if (lancamento.Tipo == 'C')
                    consolidado.TotalCreditos += lancamento.Valor;
                else if (lancamento.Tipo == 'D')
                    consolidado.TotalDebitos += lancamento.Valor;

                lancamento.Status = "Consolidado";
                await _context.SaveChangesAsync();

                await db.KeyDeleteAsync("consolidado:" + lancamento.DataLancamento.ToString("yyyy-MM-dd"));
            }

            return lancamento.IdLancamento;
        }

        private async Task<bool> ServiceConsolidadoDisponivel()
        {
            using var activity = _activity.StartActivity(
                "HTTP.GET.Consolidado.Health",
                ActivityKind.Client);

            try
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                var response = await _httpClient.GetAsync("http://localhost:8080/health");

                activity?.SetTag("http.status_code", (int)response.StatusCode);
                activity?.SetStatus(response.IsSuccessStatusCode
                    ? ActivityStatusCode.Ok
                    : ActivityStatusCode.Error);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _consolidadoIndisponivelCounter.Add(1);
                activity?.RecordException(ex);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Erro ao verificar serviço de consolidado");
                return false;
            }
        }
    }
}