using Enumeration;

namespace Contract.Query;

public class LancamentoQueryResult
{
    public int Id { get; set; }
    public DateTime DataLancamento { get; set; }
    public TipoLancamento Tipo { get; set; }
    public decimal Valor { get; set; }
    public string? Descricao { get; set; }

}