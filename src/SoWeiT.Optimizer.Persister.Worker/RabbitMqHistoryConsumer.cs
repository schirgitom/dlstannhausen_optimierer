using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SoWeiT.Optimizer.Messaging.RabbitMq;
using SoWeiT.Optimizer.Persistence.History.Persistence;

namespace SoWeiT.Optimizer.Persister.Worker;

public sealed class RabbitMqHistoryConsumer : BackgroundService
{
    private readonly ILogger<RabbitMqHistoryConsumer> _logger;
    private readonly IOptimizerHistoryStore _historyStore;
    private readonly RabbitMqHistoryOptions _options;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqHistoryConsumer(
        IConfiguration configuration,
        IOptimizerHistoryStore historyStore,
        ILogger<RabbitMqHistoryConsumer> logger)
    {
        _historyStore = historyStore;
        _logger = logger;
        _options = configuration.GetSection("RabbitMq").Get<RabbitMqHistoryOptions>() ?? new RabbitMqHistoryOptions();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        _connection = factory.CreateConnection("optimizer-persister-history-consumer");
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(queue: _options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (_, ea) => HandleMessage(ea);

        _channel.BasicConsume(queue: _options.QueueName, autoAck: false, consumer: consumer);
        _logger.LogInformation("History consumer connected to RabbitMQ queue {QueueName}", _options.QueueName);

        stoppingToken.Register(() =>
        {
            try
            {
                _channel?.Close();
                _connection?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing RabbitMQ connection");
            }
        });

        return Task.CompletedTask;
    }

    private void HandleMessage(BasicDeliverEventArgs args)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var body = Encoding.UTF8.GetString(args.Body.ToArray());
            var message = JsonSerializer.Deserialize<OptimizerHistoryEvent>(body, _serializerOptions);
            if (message is null)
            {
                throw new InvalidOperationException("Could not deserialize history event message.");
            }

            switch (message.EventType)
            {
                case OptimizerHistoryEventType.SessionCreated:
                    if (message.SessionConfig is null)
                    {
                        throw new InvalidOperationException("SessionCreated event is missing SessionConfig.");
                    }

                    _historyStore.CreateSession(
                        message.SessionId,
                        message.SessionConfig,
                        message.CreatedAtUtc ?? DateTime.UtcNow);
                    break;

                case OptimizerHistoryEventType.SessionEnded:
                    _historyStore.MarkSessionEnded(
                        message.SessionId,
                        message.EndedAtUtc ?? DateTime.UtcNow);
                    break;

                case OptimizerHistoryEventType.RequestAppended:
                    if (message.Request is null)
                    {
                        throw new InvalidOperationException("RequestAppended event is missing Request.");
                    }

                    _historyStore.AppendRequest(message.SessionId, message.Request);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported event type '{message.EventType}'.");
            }

            _channel.BasicAck(deliveryTag: args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing history event; message will be requeued.");
            _channel.BasicNack(deliveryTag: args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
