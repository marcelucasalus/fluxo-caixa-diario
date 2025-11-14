using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandStore.FluxoCaixa;
using FluxoCaixa.LancamentoRegistrar.Entity;
using Integration.Sub;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using Xunit;

namespace TestesUnitarios.Worker
{
    public class ConsolidadoWorkerTests : IDisposable
    {
        private readonly Mock<IConnection> _mockRabbitConnection;
        private readonly Mock<IModel> _mockChannel;
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<ILogger<ConsolidadoWorker>> _mockLogger;
        private readonly Mock<IConnectionMultiplexer> _mockRedis;
        private readonly Mock<IDatabase> _mockRedisDb;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private FluxoCaixaContext _dbContext;

        public ConsolidadoWorkerTests()
        {
            _mockRabbitConnection = new Mock<IConnection>();
            _mockChannel = new Mock<IModel>();
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockLogger = new Mock<ILogger<ConsolidadoWorker>>();
            _mockRedis = new Mock<IConnectionMultiplexer>();
            _mockRedisDb = new Mock<IDatabase>();
            _mockServiceProvider = new Mock<IServiceProvider>();

            _mockRabbitConnection.Setup(x => x.CreateModel()).Returns(_mockChannel.Object);
            _mockRedis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_mockRedisDb.Object);

            SetupDatabase();
        }

        private void SetupDatabase()
        {
            var options = new DbContextOptionsBuilder<FluxoCaixaContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new FluxoCaixaContext(options);
        }

        public void Dispose()
        {
            _dbContext?.Dispose();
        }

        [Fact]
        public async Task ExecuteAsync_WhenServiceUnavailable_ShouldNackMessage()
        {
            // Arrange
            var worker = new ConsolidadoWorker(_mockRabbitConnection.Object, _mockScopeFactory.Object, _mockLogger.Object);
            var lancamento = new Lancamento
            {
                IdLancamento = 1,
                Tipo = 'C',
                Valor = 100m,
                DataLancamento = DateTime.Now,
                DataConsolidacao = DateTime.Now.Date,
                Status = "Pendente",
                Descricao = "Test"
            };
            var json = JsonSerializer.Serialize(lancamento);
            var body = Encoding.UTF8.GetBytes(json);
            var basicDeliverEventArgs = CreateBasicDeliverEventArgs(body);

            AsyncEventingBasicConsumer capturedConsumer = null;
            
            _mockChannel.Setup(x => x.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>>(),
                (IBasicConsumer)It.IsAny<IAsyncBasicConsumer>()
            )).Callback<string, bool, string, bool, bool, IDictionary<string, object>, IAsyncBasicConsumer>(
                (queue, autoAck, consumerTag, noLocal, exclusive, args, basicConsumer) =>
                {
                    capturedConsumer = basicConsumer as AsyncEventingBasicConsumer;
                }
            );

            MockServiceScope(lancamento);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            // Act
            var task = worker.StartAsync(cts.Token);
            await Task.Delay(100);

            if (capturedConsumer != null)
            {
                await capturedConsumer.HandleBasicDeliver(
                    basicDeliverEventArgs.ConsumerTag,
                    basicDeliverEventArgs.DeliveryTag,
                    basicDeliverEventArgs.Redelivered,
                    basicDeliverEventArgs.Exchange,
                    basicDeliverEventArgs.RoutingKey,
                    basicDeliverEventArgs.BasicProperties,
                    basicDeliverEventArgs.Body
                );
            }

            // Assert
            _mockChannel.Verify(x => x.BasicNack(
                It.IsAny<ulong>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()
            ), Times.Once, "Message should be nacked when service is unavailable");
        }

        [Fact]
        public async Task ExecuteAsync_WhenConsolidadoNotFound_ShouldNackMessage()
        {
            // Arrange
            var worker = new ConsolidadoWorker(_mockRabbitConnection.Object, _mockScopeFactory.Object, _mockLogger.Object);
            var lancamento = new Lancamento
            {
                IdLancamento = 1,
                Tipo = 'C',
                Valor = 100m,
                DataLancamento = DateTime.Now,
                DataConsolidacao = DateTime.Now.Date,
                Status = "Pendente",
                Descricao = "Test"
            };
            var json = JsonSerializer.Serialize(lancamento);
            var body = Encoding.UTF8.GetBytes(json);
            var basicDeliverEventArgs = CreateBasicDeliverEventArgs(body);

            AsyncEventingBasicConsumer capturedConsumer = null;

            _mockChannel.Setup(x => x.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>>(),
                (IBasicConsumer)It.IsAny<IAsyncBasicConsumer>()
            )).Callback<string, bool, string, bool, bool, IDictionary<string, object>, IAsyncBasicConsumer>(
                (queue, autoAck, consumerTag, noLocal, exclusive, args, basicConsumer) =>
                {
                    capturedConsumer = basicConsumer as AsyncEventingBasicConsumer;
                }
            );

            var mockScope = new Mock<IServiceScope>();
            mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);

            // Setup service provider to return mocks without consolidado in DB
            _mockServiceProvider.Setup(x => x.GetService(typeof(FluxoCaixaContext)))
                .Returns(_dbContext);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IConnectionMultiplexer)))
                .Returns(_mockRedis.Object);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            // Act
            var task = worker.StartAsync(cts.Token);
            await Task.Delay(100);

            if (capturedConsumer != null)
            {
                await capturedConsumer.HandleBasicDeliver(
                    basicDeliverEventArgs.ConsumerTag,
                    basicDeliverEventArgs.DeliveryTag,
                    basicDeliverEventArgs.Redelivered,
                    basicDeliverEventArgs.Exchange,
                    basicDeliverEventArgs.RoutingKey,
                    basicDeliverEventArgs.BasicProperties,
                    basicDeliverEventArgs.Body
                );
            }

            // Assert
            _mockChannel.Verify(x => x.BasicNack(
                It.IsAny<ulong>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()
            ), Times.Once, "Message should be nacked when consolidado is not found");
        }

        [Fact]
        public async Task ExecuteAsync_WhenProcessingCredit_ShouldUpdateTotalCreditos()
        {
            // Arrange
            var worker = new ConsolidadoWorker(_mockRabbitConnection.Object, _mockScopeFactory.Object, _mockLogger.Object);
            var dataConsolidacao = DateTime.Now.Date;
            
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataConsolidacao,
                TotalCreditos = 100m,
                TotalDebitos = 50m
            };
            
            var lancamento = new Lancamento
            {
                IdLancamento = 1,
                Tipo = 'C',
                Valor = 75m,
                DataLancamento = dataConsolidacao,
                DataConsolidacao = dataConsolidacao,
                Status = "Pendente",
                Descricao = "Test Credit"
            };

            _dbContext.ConsolidadosDiarios.Add(consolidado);
            _dbContext.Lancamentos.Add(lancamento);
            await _dbContext.SaveChangesAsync();

            var json = JsonSerializer.Serialize(lancamento);
            var body = Encoding.UTF8.GetBytes(json);
            var basicDeliverEventArgs = CreateBasicDeliverEventArgs(body);

            AsyncEventingBasicConsumer capturedConsumer = null;

            _mockChannel.Setup(x => x.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>>(),
                (IBasicConsumer)It.IsAny<IAsyncBasicConsumer>()
            )).Callback<string, bool, string, bool, bool, IDictionary<string, object>, IAsyncBasicConsumer>(
                (queue, autoAck, consumerTag, noLocal, exclusive, args, basicConsumer) =>
                {
                    capturedConsumer = basicConsumer as AsyncEventingBasicConsumer;
                }
            );

            MockServiceScope(lancamento);

            _mockRedisDb.Setup(x => x.KeyDeleteAsync(It.IsAny<string>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1 == await Task.FromResult(1));


            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            // Act
            var task = worker.StartAsync(cts.Token);
            await Task.Delay(100);

            if (capturedConsumer != null)
            {
                await capturedConsumer.HandleBasicDeliver(
                    basicDeliverEventArgs.ConsumerTag,
                    basicDeliverEventArgs.DeliveryTag,
                    basicDeliverEventArgs.Redelivered,
                    basicDeliverEventArgs.Exchange,
                    basicDeliverEventArgs.RoutingKey,
                    basicDeliverEventArgs.BasicProperties,
                    basicDeliverEventArgs.Body
                );
            }

            // Assert
            _mockChannel.Verify(x => x.BasicAck(
                It.IsAny<ulong>(),
                It.IsAny<bool>()
            ), Times.Once, "Message should be acknowledged after successful processing");

            var updatedConsolidado = await _dbContext.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataConsolidacao);
            Assert.NotNull(updatedConsolidado);
            Assert.Equal(175m, updatedConsolidado.TotalCreditos);
        }

        [Fact]
        public async Task ExecuteAsync_WhenProcessingDebit_ShouldUpdateTotalDebitos()
        {
            // Arrange
            var worker = new ConsolidadoWorker(_mockRabbitConnection.Object, _mockScopeFactory.Object, _mockLogger.Object);
            var dataConsolidacao = DateTime.Now.Date;
            
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataConsolidacao,
                TotalCreditos = 100m,
                TotalDebitos = 50m
            };
            
            var lancamento = new Lancamento
            {
                IdLancamento = 1,
                Tipo = 'D',
                Valor = 25m,
                DataLancamento = dataConsolidacao,
                DataConsolidacao = dataConsolidacao,
                Status = "Pendente",
                Descricao = "Test Debit"
            };

            _dbContext.ConsolidadosDiarios.Add(consolidado);
            _dbContext.Lancamentos.Add(lancamento);
            await _dbContext.SaveChangesAsync();

            var json = JsonSerializer.Serialize(lancamento);
            var body = Encoding.UTF8.GetBytes(json);
            var basicDeliverEventArgs = CreateBasicDeliverEventArgs(body);

            AsyncEventingBasicConsumer capturedConsumer = null;

            _mockChannel.Setup(x => x.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>>(),
                (IBasicConsumer)It.IsAny<IAsyncBasicConsumer>()
            )).Callback<string, bool, string, bool, bool, IDictionary<string, object>, IAsyncBasicConsumer>(
                (queue, autoAck, consumerTag, noLocal, exclusive, args, basicConsumer) =>
                {
                    capturedConsumer = basicConsumer as AsyncEventingBasicConsumer;
                }
            );

            MockServiceScope(lancamento);

            _mockRedisDb.Setup(x => x.KeyDeleteAsync(It.IsAny<string>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1 == await Task.FromResult(1));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            // Act
            var task = worker.StartAsync(cts.Token);
            await Task.Delay(100);

            if (capturedConsumer != null)
            {
                await capturedConsumer.HandleBasicDeliver(
                    basicDeliverEventArgs.ConsumerTag,
                    basicDeliverEventArgs.DeliveryTag,
                    basicDeliverEventArgs.Redelivered,
                    basicDeliverEventArgs.Exchange,
                    basicDeliverEventArgs.RoutingKey,
                    basicDeliverEventArgs.BasicProperties,
                    basicDeliverEventArgs.Body
                );
            }

            // Assert
            _mockChannel.Verify(x => x.BasicAck(
                It.IsAny<ulong>(),
                It.IsAny<bool>()
            ), Times.Once, "Message should be acknowledged after successful processing");

            var updatedConsolidado = await _dbContext.ConsolidadosDiarios
                .FirstOrDefaultAsync(c => c.DataConsolidacao == dataConsolidacao);
            Assert.NotNull(updatedConsolidado);
            Assert.Equal(75m, updatedConsolidado.TotalDebitos);
        }

        [Fact]
        public async Task ExecuteAsync_WhenProcessingComplete_ShouldUpdateLancamentoStatus()
        {
            // Arrange
            var worker = new ConsolidadoWorker(_mockRabbitConnection.Object, _mockScopeFactory.Object, _mockLogger.Object);
            var dataConsolidacao = DateTime.Now.Date;
            
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataConsolidacao,
                TotalCreditos = 100m,
                TotalDebitos = 50m
            };
            
            var lancamento = new Lancamento
            {
                IdLancamento = 1,
                Tipo = 'C',
                Valor = 50m,
                DataLancamento = dataConsolidacao,
                DataConsolidacao = dataConsolidacao,
                Status = "Pendente",
                Descricao = "Test"
            };

            _dbContext.ConsolidadosDiarios.Add(consolidado);
            _dbContext.Lancamentos.Add(lancamento);
            await _dbContext.SaveChangesAsync();

            var json = JsonSerializer.Serialize(lancamento);
            var body = Encoding.UTF8.GetBytes(json);
            var basicDeliverEventArgs = CreateBasicDeliverEventArgs(body);

            AsyncEventingBasicConsumer capturedConsumer = null;

            _mockChannel.Setup(x => x.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>>(),
                (IBasicConsumer)It.IsAny<IAsyncBasicConsumer>()
            )).Callback<string, bool, string, bool, bool, IDictionary<string, object>, IAsyncBasicConsumer>(
                (queue, autoAck, consumerTag, noLocal, exclusive, args, basicConsumer) =>
                {
                    capturedConsumer = basicConsumer as AsyncEventingBasicConsumer;
                }
            );

            MockServiceScope(lancamento);

            _mockRedisDb.Setup(x => x.KeyDeleteAsync(It.IsAny<string>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1 == await Task.FromResult(1));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            // Act
            var task = worker.StartAsync(cts.Token);
            await Task.Delay(100);

            if (capturedConsumer != null)
            {
                await capturedConsumer.HandleBasicDeliver(
                    basicDeliverEventArgs.ConsumerTag,
                    basicDeliverEventArgs.DeliveryTag,
                    basicDeliverEventArgs.Redelivered,
                    basicDeliverEventArgs.Exchange,
                    basicDeliverEventArgs.RoutingKey,
                    basicDeliverEventArgs.BasicProperties,
                    basicDeliverEventArgs.Body
                );
            }

            // Assert
            var updatedLancamento = await _dbContext.Lancamentos
                .FirstOrDefaultAsync(l => l.IdLancamento == 1);
            Assert.NotNull(updatedLancamento);
            Assert.Equal("Consolidado", updatedLancamento.Status);
        }

        [Fact]
        public async Task ExecuteAsync_WhenProcessingComplete_ShouldInvalidateRedisCache()
        {
            // Arrange
            var worker = new ConsolidadoWorker(_mockRabbitConnection.Object, _mockScopeFactory.Object, _mockLogger.Object);
            var dataConsolidacao = DateTime.Now.Date;
            
            var consolidado = new ConsolidadoDiario
            {
                DataConsolidacao = dataConsolidacao,
                TotalCreditos = 100m,
                TotalDebitos = 50m
            };
            
            var lancamento = new Lancamento
            {
                IdLancamento = 1,
                Tipo = 'C',
                Valor = 50m,
                DataLancamento = dataConsolidacao,
                DataConsolidacao = dataConsolidacao,
                Status = "Pendente",
                Descricao = "Test"
            };

            _dbContext.ConsolidadosDiarios.Add(consolidado);
            _dbContext.Lancamentos.Add(lancamento);
            await _dbContext.SaveChangesAsync();

            var json = JsonSerializer.Serialize(lancamento);
            var body = Encoding.UTF8.GetBytes(json);
            var basicDeliverEventArgs = CreateBasicDeliverEventArgs(body);

            AsyncEventingBasicConsumer capturedConsumer = null;

            _mockChannel.Setup(x => x.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>>(),
                (IBasicConsumer)It.IsAny<IAsyncBasicConsumer>()
            )).Callback<string, bool, string, bool, bool, IDictionary<string, object>, IAsyncBasicConsumer>(
                (queue, autoAck, consumerTag, noLocal, exclusive, args, basicConsumer) =>
                {
                    capturedConsumer = basicConsumer as AsyncEventingBasicConsumer;
                }
            );

            MockServiceScope(lancamento);

            _mockRedisDb.Setup(x => x.KeyDeleteAsync(It.IsAny<string>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1 == await Task.FromResult(1));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            // Act
            var task = worker.StartAsync(cts.Token);
            await Task.Delay(100);

            if (capturedConsumer != null)
            {
                await capturedConsumer.HandleBasicDeliver(
                    basicDeliverEventArgs.ConsumerTag,
                    basicDeliverEventArgs.DeliveryTag,
                    basicDeliverEventArgs.Redelivered,
                    basicDeliverEventArgs.Exchange,
                    basicDeliverEventArgs.RoutingKey,
                    basicDeliverEventArgs.BasicProperties,
                    basicDeliverEventArgs.Body
                );
            }

            // Assert
            _mockRedisDb.Verify(x => x.KeyDeleteAsync(
                It.Is<string>(s => s.StartsWith("consolidado:")),
                It.IsAny<CommandFlags>()
            ), Times.Once, "Consolidado cache key should be invalidated");

            _mockRedisDb.Verify(x => x.KeyDeleteAsync(
                It.Is<string>(s => s.StartsWith("lancamento:")),
                It.IsAny<CommandFlags>()
            ), Times.Once, "Lancamento cache key should be invalidated");
        }

        private void MockServiceScope(Lancamento lancamento)
        {
            var mockScope = new Mock<IServiceScope>();
            mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);

            _mockServiceProvider.Setup(x => x.GetService(typeof(FluxoCaixaContext)))
                .Returns(_dbContext);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IConnectionMultiplexer)))
                .Returns(_mockRedis.Object);
        }

        private BasicDeliverEventArgs CreateBasicDeliverEventArgs(byte[] body)
        {
            return new BasicDeliverEventArgs
            {
                Body = new ReadOnlyMemory<byte>(body),
                DeliveryTag = 1,
                Redelivered = false,
                Exchange = "lancamentos",
                RoutingKey = "lancamento.registrado"
            };
        }
    }
}
