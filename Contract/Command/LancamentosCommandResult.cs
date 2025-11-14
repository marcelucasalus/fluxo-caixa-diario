using Enumeration;

namespace Contract.Command
{
    public class LancamentosCommandResult
    {
        public int Id { get; set; }
        public DateTime DataLancamento { get; set; }
        public TipoLancamento Tipo { get; set; }
        public decimal Valor { get; set; }
        public string? Descricao { get; set; }

    }
}
