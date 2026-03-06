using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoWeiT.Optimizer.Persistence.Redis.Models;
using StackExchange.Redis;

namespace SoWeiT.Optimizer.Persistence.Redis.Persistence;

public sealed class RedisOptimizerStateStore : IOptimizerStateStore
{
    private const string KeyPrefix = "optimizer:sessions:";

    private readonly IDatabase _database;
    private readonly ILogger<RedisOptimizerStateStore> _logger;
    private readonly TimeSpan _ttl;

    public RedisOptimizerStateStore(IConnectionMultiplexer multiplexer, ILogger<RedisOptimizerStateStore> logger, IConfiguration configuration)
    {
        _database = multiplexer.GetDatabase();
        _logger = logger;

        var ttlMinutes = configuration.GetValue<int?>("OptimizerStateStore:SessionTtlMinutes") ?? 720;
        _ttl = TimeSpan.FromMinutes(ttlMinutes);
    }

    public bool TryLoad(Guid sessionId, out PersistedOptimizerSession? session)
    {
        _logger.LogInformation("Redis load start: SessionId={SessionId}", sessionId);
        var json = _database.StringGet(GetKey(sessionId));
        if (json.IsNullOrEmpty)
        {
            session = null;
            _logger.LogInformation("Redis load miss: SessionId={SessionId}", sessionId);
            return false;
        }

        session = JsonSerializer.Deserialize<PersistedOptimizerSession>(json!);
        var found = session is not null;
        _logger.LogInformation("Redis load done: SessionId={SessionId}, Found={Found}", sessionId, found);
        return found;
    }

    public void Save(PersistedOptimizerSession session)
    {
        _logger.LogInformation("Redis save start: SessionId={SessionId}", session.SessionId);
        var json = JsonSerializer.Serialize(session);
        _database.StringSet(GetKey(session.SessionId), json, _ttl);
        _logger.LogInformation("Redis save done: SessionId={SessionId}, TtlMinutes={TtlMinutes}", session.SessionId, _ttl.TotalMinutes);
    }

    public void Touch(Guid sessionId)
    {
        _logger.LogInformation("Redis touch start: SessionId={SessionId}", sessionId);
        _database.KeyExpire(GetKey(sessionId), _ttl);
        _logger.LogInformation("Redis touch done: SessionId={SessionId}, TtlMinutes={TtlMinutes}", sessionId, _ttl.TotalMinutes);
    }

    public void Delete(Guid sessionId)
    {
        _logger.LogInformation("Redis delete start: SessionId={SessionId}", sessionId);
        _database.KeyDelete(GetKey(sessionId));
        _logger.LogInformation("Redis delete done: SessionId={SessionId}", sessionId);
    }

    private static string GetKey(Guid sessionId) => KeyPrefix + sessionId.ToString("D");
}

