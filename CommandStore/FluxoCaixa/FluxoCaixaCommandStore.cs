using FluxoCaixa.LancamentoRegistrar.Entity;
using FluxoCaixa.LancamentoRegistrar.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Net.Http;

namespace CommandStore.FluxoCaixa
{
    public class FluxoCaixaCommandStore : IFluxoCaixaCommandStore
    {
        private readonly FluxoCaixaContext _context;
        private readonly IConnectionMultiplexer _redis;
        private readonly IRabbitMqPublisher _rabbitPublisher;
        private readonly ILogger<FluxoCaixaCommandStore> _logger;
        private readonly HttpClient _httpClient;

        public FluxoCaixaCommandStore(FluxoCaixaContext context, IConnectionMultiplexer redis, IRabbitMqPublisher rabbitPublisher, ILogger<FluxoCaixaCommandStore> logger, HttpClient httpClient)
        {
            _context = context;
            _redis = redis;
            _rabbitPublisher = rabbitPublisher;
            _logger = logger;
            _httpClient = httpClient;
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
                    _logger.LogInformation("Cadastrando consolidado.");
                    consolidado = new ConsolidadoDiario
                    {
                        DataConsolidacao = lancamento.DataLancamento.Date,
                        TotalCreditos = 0,
                        TotalDebitos = 0
                    };
                    _logger.LogInformation("Validando registro de lancamento.");
                    await _context.ConsolidadosDiarios.AddAsync(consolidado);
                    return await ValidaLancamento(lancamento, consolidado, db);
                }
                
                return await ValidaLancamento(lancamento, consolidado, db);                                
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao registrar lançamento: {ex.Message}");
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
                _logger.LogInformation("Servico fora do ar, publicando na fila.");
                lancamento.Status = "Pendente";  // Marca como pendente
                await _context.SaveChangesAsync();                                      
                _rabbitPublisher.PublishLancamento(lancamento);
                _logger.LogInformation("Publicado na fila.");
            }
            else
            {
                _logger.LogInformation("Lancamento consolidado");
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
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                var response = await _httpClient.GetAsync("http://localhost:8080/health");
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
