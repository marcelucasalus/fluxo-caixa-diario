using Contract.Query;
using MediatR;
using QueryStore.Interface;

namespace Query.ConsolidadoDiario;

public sealed class ConsolidadoQueryHandler : IRequestHandler<ConsolidadoQuery, ConsolidadoQueryResult>
{
    public readonly IConsolidadoQueryStore _store;

    public ConsolidadoQueryHandler(IConsolidadoQueryStore store)
    {
        _store = store;
    }

    public async Task<ConsolidadoQueryResult> Handle(ConsolidadoQuery request, CancellationToken cancellationToken)
    {
        return await _store.ObterConsolidadoDiario(request.Data);
    }
}