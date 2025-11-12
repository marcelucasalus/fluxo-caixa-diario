using Enumeration;
using MediatR;

namespace Contract.Command;

public class LancamentoRegistrarCommand : IRequest<string>
{
    public DateTime DataLancamento { get; set; }

    public TipoLancamento Tipo { get; set; }

    public decimal Valor { get; set; }
    public string? Descrição { get; set; }
}