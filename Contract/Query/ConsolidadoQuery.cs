using MediatR;

namespace Contract.Query;

public sealed class ConsolidadoQuery : IRequest<ConsolidadoQueryResult>
{
    public DateTime Data { get; set; }
}
