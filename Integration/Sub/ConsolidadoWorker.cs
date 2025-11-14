using CommandStore.FluxoCaixa;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace Integration.Sub
{
    public class ConsolidadoWorker : BackgroundService
    {
        private readonly IConnection _rabbitConnection;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ConsolidadoWorker> _logger;

        public ConsolidadoWorker(IConnection rabbitConnection, IServiceScopeFactory scopeFactory, ILogger<ConsolidadoWorker> logger)
        {
            _rabbitConnection = rabbitConnection;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var channel = _rabbitConnection.CreateModel();

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<FluxoCaixaContext>();
                var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

                var lancamento = JsonSerializer.Deserialize<Lancamento>(json);

                // Simula verificação do serviço
                var consolidadoDisponivel = await ServiceConsolidadoDisponivel();
                if (!consolidadoDisponivel)
                {                    
                    channel.BasicNack(ea.DeliveryTag, false, true);
                    return;
                }
                _logger.LogInformation("Servico disponivel.");

                var consolidado = await context.ConsolidadosDiarios
                    .FirstOrDefaultAsync(c => c.DataConsolidacao.Date == lancamento.DataConsolidacao.Date);

                if (consolidado == null)
                {
                    channel.BasicNack(ea.DeliveryTag, false, true);
                    return;
                }
                var lancamentoEntity = await context.Lancamentos.Where(l => l.IdLancamento == lancamento.IdLancamento).FirstOrDefaultAsync();

                _logger.LogInformation("Processando lancamentos e atualziando consolidado.");

                lancamentoEntity!.Status = "Consolidado";
                if (lancamento.Tipo == 'C')
                    consolidado.TotalCreditos += lancamento.Valor;
                else
                    consolidado.TotalDebitos += lancamento.Valor;

                await context.SaveChangesAsync();
                _logger.LogInformation("Invalidando cache.");
                string key = "consolidado:" + consolidado.DataConsolidacao.ToString("yyyy-MM-dd");
                await redis.KeyDeleteAsync(key);
                await redis.KeyDeleteAsync("lancamento:" + lancamento.DataLancamento.ToString("yyyy-MM-dd"));

                channel.BasicAck(ea.DeliveryTag, false);
            };

            // Important: channel.BasicConsume supports async consumer
            channel.BasicConsume("consolidado_queue", autoAck: false, consumer: consumer);



        }

        private async Task<bool> ServiceConsolidadoDisponivel()
        {
            try
            {
                _logger.LogInformation("Verificando servico de consolidado.");
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                // Use o hostname do container do serviço de consolidado
                var response = await client.GetAsync("http://localhost:8080/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

}
