using FluxoCaixa.LancamentoRegistrar.Entity;
using FluxoCaixa.LancamentoRegistrar.Interface;

namespace FluxoCaixa.LancamentoRegistrar.Service;

public class LancamentoRegistrarService : ILancamentoRegistrarService
{
    public readonly IFluxoCaixaCommandStore _store;

    public LancamentoRegistrarService(IFluxoCaixaCommandStore store)
    {
        _store = store;
    }


    public async Task<int> RegistrarLancamentos(Lancamento entity)
    {
        return await _store.RegistrarLancamentos(entity);
    }

}