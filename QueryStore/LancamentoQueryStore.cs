using CommandStore.FluxoCaixa;
using Contract.Query;
using Enumeration;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using QueryStore.Interface;
using StackExchange.Redis;
using System.Text.Json;

namespace QueryStore;

public sealed class LancamentoQueryStore : ILancamentoQueryStore
{
    private readonly FluxoCaixaContext _context;
    private readonly IConnectionMultiplexer _redis;

    public LancamentoQueryStore(FluxoCaixaContext context, IConnectionMultiplexer redis)
    {
        _context = context;
        _redis = redis;
    }

    public async Task<IEnumerable<LancamentoQueryResult>> ObterLancamento(DateTime data)
    {
        IEnumerable<Lancamento> itens = null;
        var aux = new List<LancamentoQueryResult>();
        var db = _redis.GetDatabase();
        string key = "lancamento:" + data.Date.ToString("yyyy-MM-dd");
        var json = await db.StringGetAsync(key);

        if (!json.IsNullOrEmpty)
            itens = JsonSerializer.Deserialize<IEnumerable<Lancamento>>(json.ToString());
        else
        {
            itens = await _context.Lancamentos.Where(l => l.DataLancamento.Date == data.Date).ToListAsync();
            if (itens != null && itens.Any())
            {
                await db.StringSetAsync(
                    key,
                    JsonSerializer.Serialize(itens),
                    TimeSpan.FromMinutes(30)
                );
            }
            else
                throw new Exception($"Lancamento com a data: {data.Date.ToString("yyyy-MM-dd")} nao foi encontrado!");
        }

        Parallel.ForEach(itens, item =>
        {
            aux.Add(new LancamentoQueryResult
            {
                Id = item.IdLancamento,
                Descricao = item.Descricao,
                Valor = item.Valor,
                DataLancamento = item.DataLancamento,
                Tipo = item.Tipo == 'C' ? TipoLancamento.Credito : TipoLancamento.Debito,
            });
        });

        return aux;
    }

}