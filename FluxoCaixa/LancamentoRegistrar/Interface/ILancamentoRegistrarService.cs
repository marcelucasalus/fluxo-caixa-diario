using FluxoCaixa.LancamentoRegistrar.Entity;

namespace FluxoCaixa.LancamentoRegistrar.Interface;

public interface ILancamentoRegistrarService
{
    public Task<int> RegistrarLancamentos(Lancamento entity);
}