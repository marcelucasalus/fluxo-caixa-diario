using FluxoCaixa.LancamentoRegistrar.Entity;

public interface IRabbitMqPublisher
{
    void PublishLancamento(Lancamento lancamento);
}