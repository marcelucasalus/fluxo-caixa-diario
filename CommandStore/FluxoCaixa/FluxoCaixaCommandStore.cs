using FluxoCaixa.LancamentoRegistrar.Entity;
using FluxoCaixa.LancamentoRegistrar.Interface;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CommandStore.FluxoCaixa
{
    public class FluxoCaixaCommandStore : IFluxoCaixaCommandStore
    {
        private readonly FluxoCaixaContext _context;
        private readonly IConnectionMultiplexer _redis;
        private readonly RabbitMqPublisher _rabbitPublisher;

        public FluxoCaixaCommandStore(FluxoCaixaContext context, IConnectionMultiplexer redis, RabbitMqPublisher rabbitPublisher)
        {
            _context = context;
            _redis = redis;
            _rabbitPublisher = rabbitPublisher;
        }

        public async Task<int> RegistrarLancamentos(Lancamento lancamento)
        {
            try
            {
                var db = _redis.GetDatabase();
                // Verificar se existe consolidado diário está disponível
                var consolidado = await _context.ConsolidadosDiarios.FirstOrDefaultAsync(c => c.DataConsolidacao.Date == lancamento.DataLancamento.Date);

                if (consolidado == null)
                {
                    consolidado = new ConsolidadoDiario
                    {
                        DataConsolidacao = lancamento.DataLancamento.Date,
                        TotalCreditos = 0,
                        TotalDebitos = 0
                    };
                    await _context.ConsolidadosDiarios.AddAsync(consolidado);
                    return await ValidaLancamento(lancamento, consolidado, db);
                }
                
                return await ValidaLancamento(lancamento, consolidado, db);                                
            }
            catch (Exception ex)
            {
                // Log ou tratamento em caso de falha
                //_logger.LogError($"Erro ao registrar lançamento: {ex.Message}");
                throw;
            }
        }

        private async Task<int> ValidaLancamento(Lancamento lancamento, ConsolidadoDiario consolidado, IDatabase db)
        {
            lancamento.DataConsolidacao = consolidado.DataConsolidacao;
            await _context.Lancamentos.AddAsync(lancamento);
            await db.KeyDeleteAsync("lancamento:" + lancamento.DataLancamento.ToString("yyyy-MM-dd"));

            // Se o consolidado não existe e o serviço está indisponível, marca como pendente
            if (!await ServiceConsolidadoDisponivel())
            {
                lancamento.Status = "Pendente";  // Marca como pendente
                await _context.SaveChangesAsync();                                      
                _rabbitPublisher.PublishLancamento(lancamento);
            }
            else
            {
                if (lancamento.Tipo == 'C')
                {
                    consolidado.TotalCreditos += lancamento.Valor;
                }
                else if (lancamento.Tipo == 'D')
                {
                    consolidado.TotalDebitos += lancamento.Valor;
                }
                lancamento.Status = "Consolidado";  // Marca como processado
                await _context.SaveChangesAsync();
                await db.KeyDeleteAsync("consolidado:" + lancamento.DataLancamento.ToString("yyyy-MM-dd"));
            }
            
            return lancamento.IdLancamento;
        }
        private async Task<bool> ServiceConsolidadoDisponivel()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Use o hostname do container do serviço de consolidado
                var response = await client.GetAsync("http://localhost:8080/health");
                return response.IsSuccessStatusCode;
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
        }

    }

}
