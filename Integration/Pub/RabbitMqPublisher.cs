using FluxoCaixa.LancamentoRegistrar.Entity;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

public class RabbitMqPublisher : IRabbitMqPublisher
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(IConnection connection, ILogger<RabbitMqPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public void PublishLancamento(Lancamento lancamento)
    {
        _logger.LogInformation($"Iniciando publicacao da mensagem.");

        using var channel = _connection.CreateModel();

        channel.ExchangeDeclare("lancamentos", ExchangeType.Direct, durable: true);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(lancamento));

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;

        channel.BasicPublish(
            exchange: "lancamentos",
            routingKey: "lancamento.registrado",
            basicProperties: properties,
            body: body
        );

        _logger.LogInformation($"Mensagem publicada com sucesso!");
    }
}
