namespace FluxoCaixa.LancamentoRegistrar.Entity;

public sealed class ConsolidadoDiario
{
    public DateTime DataConsolidacao { get; set; }  // PK
    public decimal TotalCreditos { get; set; }
    public decimal TotalDebitos { get; set; }
    public decimal Saldo => TotalCreditos - TotalDebitos;

    // Relação 1:N com Lancamento
    public ICollection<Lancamento> Lancamentos { get; set; }
}