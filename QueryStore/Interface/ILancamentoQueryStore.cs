using Contract.Query;

namespace QueryStore.Interface;

public interface ILancamentoQueryStore
{
    Task<IEnumerable<LancamentoQueryResult>> ObterLancamento(DateTime data);
}