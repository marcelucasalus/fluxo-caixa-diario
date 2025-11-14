using CommandStore.FluxoCaixa;
using Contract.Query;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using QueryStore.Interface;
using StackExchange.Redis;
using System.Text.Json;

namespace QueryStore;

public sealed class ConsolidadoQueryStore : IConsolidadoQueryStore
{
    private readonly FluxoCaixaContext _context;
    private readonly IConnectionMultiplexer _redis;

    public ConsolidadoQueryStore(FluxoCaixaContext context, IConnectionMultiplexer redis)
    {
        _context = context;
        _redis = redis;
    }

    public async Task<ConsolidadoQueryResult> ObterConsolidadoDiario(DateTime data)
    {
        var item = new ConsolidadoDiario() { DataConsolidacao = data.Date, TotalCreditos = 0, TotalDebitos = 0 };
        var db = _redis.GetDatabase();
        string key = "consolidado:"+data.Date.ToString("yyyy-MM-dd");
        var json = await db.StringGetAsync(key);

        if (!json.IsNullOrEmpty)
            item = JsonSerializer.Deserialize<ConsolidadoDiario>(json.ToString());
        else
        {
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


        return new ConsolidadoQueryResult() { DataConsolidacao = item.DataConsolidacao, TotalCreditos = item.TotalCreditos, TotalDebitos = item.TotalDebitos };
    }

}
