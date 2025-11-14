using FluxoCaixa.LancamentoRegistrar.Entity;

namespace FluxoCaixa.LancamentoRegistrar.Interface
{
    public interface IFluxoCaixaCommandStore
    {
        public Task<int> RegistrarLancamentos(Lancamento entity);
    }
}
