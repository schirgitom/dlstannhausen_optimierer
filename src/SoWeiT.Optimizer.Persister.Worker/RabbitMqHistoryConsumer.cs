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
        _logger.LogInformation(
            "Starting RabbitMQ history consumer: Host={Host}, Port={Port}, VirtualHost={VirtualHost}, Queue={QueueName}",
            _options.HostName,
            _options.Port,
            _options.VirtualHost,
            _options.QueueName);

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
            _logger.LogDebug("Processing message {DeliveryTag}", args.DeliveryTag);
            var body = Encoding.UTF8.GetString(args.Body.ToArray());
            var message = JsonSerializer.Deserialize<OptimizerHistoryEvent>(body, _serializerOptions);
            if (message is null)
            {
                throw new InvalidOperationException("Could not deserialize history event message.");
            }

            _logger.LogDebug(
                "Received history event {EventType} for session {SessionId}",
                message.EventType,
                message.SessionId);

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
            _logger.LogDebug("Message acknowledged {DeliveryTag}", args.DeliveryTag);
        }
        catch (Exception ex)
        {
            var currentRetryCount = GetRetryCount(args.BasicProperties);
            if (currentRetryCount < _options.MaxRetryCount)
            {
                var nextRetryCount = currentRetryCount + 1;
                var retryProperties = _channel.CreateBasicProperties();
                retryProperties.Persistent = args.BasicProperties?.Persistent ?? true;
                retryProperties.ContentType = args.BasicProperties?.ContentType;
                retryProperties.ContentEncoding = args.BasicProperties?.ContentEncoding;
                retryProperties.CorrelationId = args.BasicProperties?.CorrelationId;
                retryProperties.MessageId = args.BasicProperties?.MessageId;
                retryProperties.Type = args.BasicProperties?.Type;
                retryProperties.Timestamp = args.BasicProperties?.Timestamp ?? default;
                retryProperties.Headers = CopyHeaders(args.BasicProperties?.Headers);
                retryProperties.Headers[RetryHeaderName] = nextRetryCount;

                _channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: _options.QueueName,
                    mandatory: false,
                    basicProperties: retryProperties,
                    body: args.Body);

                _logger.LogError(
                    ex,
                    "Error while processing history event; retry {RetryAttempt}/{MaxRetryCount}.",
                    nextRetryCount,
                    _options.MaxRetryCount);

                _channel.BasicAck(deliveryTag: args.DeliveryTag, multiple: false);
                return;
            }

            _logger.LogError(
                ex,
                "Error while processing history event; max retries reached ({MaxRetryCount}). Message will be rejected without requeue.",
                _options.MaxRetryCount);
            _channel.BasicNack(deliveryTag: args.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private const string RetryHeaderName = "x-history-retry-count";

    private static Dictionary<string, object> CopyHeaders(IDictionary<string, object>? originalHeaders)
    {
        var headers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (originalHeaders is null)
        {
            return headers;
        }

        foreach (var pair in originalHeaders)
        {
            headers[pair.Key] = pair.Value;
        }

        return headers;
    }

    private static int GetRetryCount(IBasicProperties? properties)
    {
        if (properties?.Headers is null || !properties.Headers.TryGetValue(RetryHeaderName, out var value))
        {
            return 0;
        }

        return value switch
        {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => (int)ui,
            long l => (int)l,
            ulong ul => (int)ul,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    public override void Dispose()
    {
        _logger.LogInformation("Disposing RabbitMQ history consumer");
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
