using Contract.Query;
using MediatR;
using QueryStore.Interface;

namespace Query.Lancamentos;

public sealed class LancamentoQueryHandler : IRequestHandler<LancamentoQuery, IEnumerable<LancamentoQueryResult>>
{
    public readonly ILancamentoQueryStore _store;

    public LancamentoQueryHandler(ILancamentoQueryStore store)
    {
        _store = store;
    }

    public async Task<IEnumerable<LancamentoQueryResult>> Handle(LancamentoQuery request, CancellationToken cancellationToken)
    {
        return await _store.ObterLancamento(request.Data);
    }
}