using MediatR;

namespace Contract.Query;

public sealed class LancamentoQuery : IRequest<IEnumerable<LancamentoQueryResult>>
{
    public DateTime Data { get; set; }
}