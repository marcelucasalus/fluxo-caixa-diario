using Enumeration;
using FluxoCaixa.LancamentoRegistrar.Entity;
using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Contract.Command;

public class LancamentosCommand : IRequest<LancamentosCommandResult>
{
    [Required]
    [EnumDataType(typeof(TipoLancamento))]
    [Range(0, 1)]
    public TipoLancamento Tipo { get; set; }
    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Valor { get; set; }
    [Required, MaxLength(255)]
    public string? Descricao { get; set; }
    [Required]
    public DateTime DataLancamento { get; set; }

    public Lancamento ToEntity()
    {
        return new Lancamento
        {
            Descricao = Descricao,
            Valor = Valor,
            Tipo = (int)Tipo == 0 ? 'C' : 'D',
            DataLancamento = DataLancamento.Date,
            DataConsolidacao = DataLancamento.Date
        };
    }
}