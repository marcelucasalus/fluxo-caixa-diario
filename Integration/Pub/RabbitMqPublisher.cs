using FluxoCaixa.LancamentoRegistrar.Entity;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

public class RabbitMqPublisher
{
    private readonly IConnection _connection;

    public RabbitMqPublisher(IConnection connection)
    {
        _connection = connection;
    }

    public void PublishLancamento(Lancamento lancamento)
    {
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

        Console.WriteLine("Mensagem publicada com sucesso!");
    }


}
