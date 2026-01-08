using CommandStore.FluxoCaixa;
using Contract.Query;
using Enumeration;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryStore.Interface;
using StackExchange.Redis;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace QueryStore;

public sealed class LancamentoQueryStore : ILancamentoQueryStore
{
    private readonly FluxoCaixaContext _context;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<LancamentoQueryStore> _logger;
    private readonly Counter<int> _queryTotalCounter;
    private readonly Counter<int> _cacheHitCounter;
    private readonly Counter<int> _cacheMissCounter;
    private readonly Counter<int> _queryErrorCounter;
    private readonly Histogram<double> _queryLatencyHistogram;
    private readonly Histogram<int> _itensRetornadosHistogram;


    public LancamentoQueryStore(FluxoCaixaContext context, IConnectionMultiplexer redis, ILogger<LancamentoQueryStore> logger, Meter meter)
    {
        _context = context;
        _redis = redis;
        _logger = logger;

        _queryTotalCounter = meter.CreateCounter<int>(
      "lancamento_query_total",
      description: "Total de consultas de lançamentos");

        _cacheHitCounter = meter.CreateCounter<int>(
            "lancamento_cache_hit_total",
            description: "Total de cache hit de lançamentos");

        _cacheMissCounter = meter.CreateCounter<int>(
            "lancamento_cache_miss_total",
            description: "Total de cache miss de lançamentos");

        _queryErrorCounter = meter.CreateCounter<int>(
            "lancamento_query_error_total",
            description: "Total de erros na consulta de lançamentos");

        _queryLatencyHistogram = meter.CreateHistogram<double>(
            "lancamento_query_latency_ms",
            unit: "ms",
            description: "Latência da consulta de lançamentos");

        _itensRetornadosHistogram = meter.CreateHistogram<int>(
            "lancamento_query_itens",
            description: "Quantidade de lançamentos retornados por consulta");
    }

    public async Task<IEnumerable<LancamentoQueryResult>> ObterLancamento(DateTime data)
    {
        var stopwatch = Stopwatch.StartNew();
        _queryTotalCounter.Add(1);

        try
        {
            IEnumerable<Lancamento> itens = null;
            var aux = new List<LancamentoQueryResult>();
            var db = _redis.GetDatabase();

            string key = "lancamento:" + data.Date.ToString("yyyy-MM-dd");
            var json = await db.StringGetAsync(key);

            if (!json.IsNullOrEmpty)
            {
                _cacheHitCounter.Add(1);
                _logger.LogDebug("Cache hit para lançamentos do dia {Data}", data.Date);

                itens = JsonSerializer.Deserialize<IEnumerable<Lancamento>>(json.ToString());
            }
            else
            {
                _cacheMissCounter.Add(1);
                _logger.LogInformation("Cache miss para lançamentos do dia {Data}. Consultando banco", data.Date);

                itens = await _context.Lancamentos
                    .Where(l => l.DataLancamento.Date == data.Date)
                    .ToListAsync();

                if (itens != null && itens.Any())
                {
                    await db.StringSetAsync(
                        key,
                        JsonSerializer.Serialize(itens),
                        TimeSpan.FromMinutes(30)
                    );
                }
                else
                {
                    throw new Exception($"Lançamentos não encontrados para {data:yyyy-MM-dd}");
                }
            }

            Parallel.ForEach(itens, item =>
            {
                aux.Add(new LancamentoQueryResult
                {
                    Id = item.IdLancamento,
                    Descricao = item.Descricao,
                    Valor = item.Valor,
                    DataLancamento = item.DataLancamento,
                    Tipo = item.Tipo == 'C'
                        ? TipoLancamento.Credito
                        : TipoLancamento.Debito
                });
            });

            _itensRetornadosHistogram.Record(aux.Count);

            return aux;
        }
        catch (Exception ex)
        {
            _queryErrorCounter.Add(1);
            _logger.LogError(ex, "Erro ao consultar lançamentos para a data {Data}", data.Date);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _queryLatencyHistogram.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }


}