using CommandStore.FluxoCaixa;
using Contract.Query;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryStore.Interface;
using StackExchange.Redis;
using System.Text.Json;

namespace QueryStore;

public sealed class ConsolidadoQueryStore : IConsolidadoQueryStore
{
    private readonly FluxoCaixaContext _context;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ConsolidadoQueryStore> _logger;

    public ConsolidadoQueryStore(FluxoCaixaContext context, IConnectionMultiplexer redis, ILogger<ConsolidadoQueryStore> logger)
    {
        _context = context;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ConsolidadoQueryResult> ObterConsolidadoDiario(DateTime data)
    {
        _logger.LogInformation($"Iniciando consulta no redis a partir da data - {data}");
        var item = new ConsolidadoDiario() { DataConsolidacao = data.Date, TotalCreditos = 0, TotalDebitos = 0 };
        var db = _redis.GetDatabase();
        string key = "consolidado:"+data.Date.ToString("yyyy-MM-dd");
        var json = await db.StringGetAsync(key);

        if (!json.IsNullOrEmpty)
            item = JsonSerializer.Deserialize<ConsolidadoDiario>(json.ToString());
        else
        {
            _logger.LogInformation($"Iniciando consulta no sql server -  {data}");
            item = await _context.ConsolidadosDiarios.FirstOrDefaultAsync(c => c.DataConsolidacao == data.Date);
            if (item != null)
            {
                await db.StringSetAsync(
                    key,
                    JsonSerializer.Serialize(item),
                    TimeSpan.FromMinutes(30)
                );
            } else
                throw new Exception($"Consolidado diario com a data: {data.Date.ToString("yyyy-MM-dd")} nao foi encontrado!");
        }

        _logger.LogInformation($"Consulta finalizada {data}");

        return new ConsolidadoQueryResult() { DataConsolidacao = item.DataConsolidacao, TotalCreditos = item.TotalCreditos, TotalDebitos = item.TotalDebitos };
    }

}
