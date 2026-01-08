using CommandStore.FluxoCaixa;
using Contract.Query;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryStore.Interface;
using StackExchange.Redis;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace QueryStore;

public sealed class ConsolidadoQueryStore : IConsolidadoQueryStore
{
    private readonly FluxoCaixaContext _context;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ConsolidadoQueryStore> _logger;
    private readonly Counter<int> _consolidadoQueryTotal;
    private readonly Counter<int> _consolidadoCacheHit;
    private readonly Counter<int> _consolidadoCacheMiss;
    private readonly Counter<int> _consolidadoQueryError;
    private readonly Histogram<double> _consolidadoQueryLatency;
    private readonly Histogram<decimal> _consolidadoCreditosHistogram;
    private readonly Histogram<decimal> _consolidadoDebitosHistogram;

    public ConsolidadoQueryStore(FluxoCaixaContext context, IConnectionMultiplexer redis, ILogger<ConsolidadoQueryStore> logger, Meter meter)
    {
        _context = context;
        _redis = redis;
        _logger = logger;

        _consolidadoQueryTotal = meter.CreateCounter<int>(
       "consolidado_query_total",
       description: "Total de consultas ao consolidado diário");

        _consolidadoCacheHit = meter.CreateCounter<int>(
            "consolidado_cache_hit_total",
            description: "Total de cache hit do consolidado diário");

        _consolidadoCacheMiss = meter.CreateCounter<int>(
            "consolidado_cache_miss_total",
            description: "Total de cache miss do consolidado diário");

        _consolidadoQueryError = meter.CreateCounter<int>(
            "consolidado_query_error_total",
            description: "Total de erros na consulta do consolidado diário");

        _consolidadoQueryLatency = meter.CreateHistogram<double>(
            "consolidado_query_latency_ms",
            unit: "ms",
            description: "Latência da consulta do consolidado diário");

        _consolidadoCreditosHistogram = meter.CreateHistogram<decimal>(
            "consolidado_total_creditos",
            description: "Valor total de créditos por consolidado");

        _consolidadoDebitosHistogram = meter.CreateHistogram<decimal>(
            "consolidado_total_debitos",
            description: "Valor total de débitos por consolidado");
    }

    public async Task<ConsolidadoQueryResult> ObterConsolidadoDiario(DateTime data)
    {
        var stopwatch = Stopwatch.StartNew();
        _consolidadoQueryTotal.Add(1);

        try
        {
            _logger.LogInformation("Consultando consolidado diário para {Data}", data.Date);

            var db = _redis.GetDatabase();
            string key = "consolidado:" + data.Date.ToString("yyyy-MM-dd");

            ConsolidadoDiario item;
            var json = await db.StringGetAsync(key);

            if (!json.IsNullOrEmpty)
            {
                _consolidadoCacheHit.Add(1);
                item = JsonSerializer.Deserialize<ConsolidadoDiario>(json.ToString());
            }
            else
            {
                _consolidadoCacheMiss.Add(1);
                item = await _context.ConsolidadosDiarios.FirstOrDefaultAsync(c => c.DataConsolidacao == data.Date);

                if (item == null)
                    throw new Exception($"Consolidado diário não encontrado para {data:yyyy-MM-dd}");

                await db.StringSetAsync(
                    key,
                    JsonSerializer.Serialize(item),
                    TimeSpan.FromMinutes(30)
                );
            }

            _consolidadoCreditosHistogram.Record(item.TotalCreditos);
            _consolidadoDebitosHistogram.Record(item.TotalDebitos);

            return new ConsolidadoQueryResult
            {
                DataConsolidacao = item.DataConsolidacao,
                TotalCreditos = item.TotalCreditos,
                TotalDebitos = item.TotalDebitos
            };
        }
        catch (Exception ex)
        {
            _consolidadoQueryError.Add(1);
            _logger.LogError(ex, "Erro ao consultar consolidado diário para {Data}", data.Date);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _consolidadoQueryLatency.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }


}
