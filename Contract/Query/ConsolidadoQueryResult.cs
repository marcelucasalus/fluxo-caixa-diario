namespace Contract.Query;

public sealed class ConsolidadoQueryResult
{
    public DateTime DataConsolidacao { get; set; }
    public decimal TotalCreditos { get; set; }
    public decimal TotalDebitos { get; set; }
    public decimal Saldo => TotalCreditos - TotalDebitos;
}