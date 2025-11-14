using Contract.Command;
using FluxoCaixa.LancamentoRegistrar.Interface;
using MediatR;

namespace Command.LancamentoRegistrar;

public sealed class LancamentoRegistrarCommandHandler : IRequestHandler<LancamentosCommand, LancamentosCommandResult>
{
    public readonly ILancamentoRegistrarService  _service;

    public LancamentoRegistrarCommandHandler(ILancamentoRegistrarService service)
    {
        _service = service;
    }

    public async Task<LancamentosCommandResult> Handle(LancamentosCommand request, CancellationToken cancellationToken)
    {
        var id = await _service.RegistrarLancamentos(request.ToEntity());
        return new LancamentosCommandResult() { Id = id, DataLancamento = request.DataLancamento, Descricao = request.Descricao, Tipo = request.Tipo, Valor = request.Valor };
    }
}