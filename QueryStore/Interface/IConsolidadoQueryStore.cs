
namespace QueryStore.Interface;

public interface IConsolidadoQueryStore
{
    Task<global::Contract.Query.ConsolidadoQueryResult> ObterConsolidadoDiario(DateTime data);
}