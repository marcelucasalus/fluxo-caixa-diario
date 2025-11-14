namespace FluxoCaixa.LancamentoRegistrar.Entity;

public sealed class Lancamento
{
    public int IdLancamento { get; set; }
    public char Tipo { get; set; }         // 'C' ou 'D'
    public decimal Valor { get; set; }
    public DateTime DataLancamento { get; set; }
    public string? Descricao { get; set; }
    public string Status { get; set; }

    public DateTime DataConsolidacao { get; set; }  // FK
}