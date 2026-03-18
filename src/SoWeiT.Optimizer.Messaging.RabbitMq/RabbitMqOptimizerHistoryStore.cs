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
    private readonly RabbitMqHistoryOptions _options;
    private readonly string _queueName;
    private readonly ConnectionFactory _factory;
    private readonly object _connectionSync = new();
    private IConnection? _connection;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public RabbitMqOptimizerHistoryStore(
        IConfiguration configuration,
        ILogger<RabbitMqOptimizerHistoryStore> logger)
    {
        _logger = logger;
        _options = configuration.GetSection("RabbitMq").Get<RabbitMqHistoryOptions>() ?? new RabbitMqHistoryOptions();
        _queueName = _options.QueueName;

        _factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
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
        try
        {
            if (!EnsureConnection())
            {
                _logger.LogError(
                    "RabbitMQ is not reachable. History event {EventType} for session {SessionId} will be skipped.",
                    payload.EventType,
                    payload.SessionId);
                return;
            }

            using var channel = _connection!.CreateModel();
            channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _serializerOptions));

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;

            channel.BasicPublish(exchange: string.Empty, routingKey: _queueName, basicProperties: properties, body: body);
            _logger.LogDebug("Published history event {EventType} for session {SessionId}", payload.EventType, payload.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not publish history event {EventType} for session {SessionId}. The application continues running.",
                payload.EventType,
                payload.SessionId);
            ResetConnection();
        }
    }

    private bool EnsureConnection()
    {
        if (_connection is { IsOpen: true })
        {
            return true;
        }

        lock (_connectionSync)
        {
            if (_connection is { IsOpen: true })
            {
                return true;
            }

            try
            {
                _connection = _factory.CreateConnection("optimizer-api-history-publisher");
                using var channel = _connection.CreateModel();
                channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                _logger.LogInformation(
                    "Connected to RabbitMQ history queue {QueueName} at {Host}:{Port}",
                    _queueName,
                    _options.HostName,
                    _options.Port);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Could not connect to RabbitMQ history queue {QueueName} at {Host}:{Port}.",
                    _queueName,
                    _options.HostName,
                    _options.Port);
                ResetConnection();
                return false;
            }
        }
    }

    private void ResetConnection()
    {
        lock (_connectionSync)
        {
            try
            {
                _connection?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disposing RabbitMQ publisher connection.");
            }
            finally
            {
                _connection = null;
            }
        }
    }

    public void Dispose()
    {
        ResetConnection();
    }
}
