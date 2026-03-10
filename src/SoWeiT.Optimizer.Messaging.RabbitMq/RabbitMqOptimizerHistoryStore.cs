using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using SoWeiT.Optimizer.Persistence.History.Persistence;

namespace SoWeiT.Optimizer.Messaging.RabbitMq;

public sealed class RabbitMqOptimizerHistoryStore : IOptimizerHistoryStore, IDisposable
{
    private readonly ILogger<RabbitMqOptimizerHistoryStore> _logger;
    private readonly string _queueName;
    private readonly IConnection _connection;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public RabbitMqOptimizerHistoryStore(
        IConfiguration configuration,
        ILogger<RabbitMqOptimizerHistoryStore> logger)
    {
        _logger = logger;
        var options = configuration.GetSection("RabbitMq").Get<RabbitMqHistoryOptions>() ?? new RabbitMqHistoryOptions();
        _queueName = options.QueueName;

        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost
        };

        _connection = factory.CreateConnection("optimizer-api-history-publisher");
        using var channel = _connection.CreateModel();
        channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
    }

    public void CreateSession(Guid sessionId, OptimizerSessionConfig sessionConfig, DateTime createdAtUtc)
    {
        Publish(new OptimizerHistoryEvent(
            OptimizerHistoryEventType.SessionCreated,
            sessionId,
            SessionConfig: sessionConfig,
            CreatedAtUtc: createdAtUtc));
    }

    public void MarkSessionEnded(Guid sessionId, DateTime endedAtUtc)
    {
        Publish(new OptimizerHistoryEvent(
            OptimizerHistoryEventType.SessionEnded,
            sessionId,
            EndedAtUtc: endedAtUtc));
    }

    public void AppendRequest(Guid sessionId, OptimizerRequestLog request)
    {
        Publish(new OptimizerHistoryEvent(
            OptimizerHistoryEventType.RequestAppended,
            sessionId,
            Request: request));
    }

    private void Publish(OptimizerHistoryEvent payload)
    {
        using var channel = _connection.CreateModel();
        channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _serializerOptions));

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;

        channel.BasicPublish(exchange: string.Empty, routingKey: _queueName, basicProperties: properties, body: body);
        _logger.LogDebug("Published history event {EventType} for session {SessionId}", payload.EventType, payload.SessionId);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
