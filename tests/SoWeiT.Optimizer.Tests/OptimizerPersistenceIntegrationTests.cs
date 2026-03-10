using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SoWeiT.Optimizer.Persistence.Redis.Persistence;
using SoWeiT.Optimizer.Service.Contracts;
using SoWeiT.Optimizer.Service.Services;
using StackExchange.Redis;

namespace SoWeiT.Optimizer.Tests;

public sealed class OptimizerPersistenceIntegrationTests
{
    private const string RedisKeyPrefix = "optimizer:sessions:";

    [Fact]
    public void SessionLifecycle_PersistsInRedisAndHistoryStore()
    {
        var redisConnectionString = Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION")
                                    ?? "localhost:6379";

        using var redis = TryConnectRedis(redisConnectionString);
        if (redis is null)
        {
            return;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OptimizerStateStore:SessionTtlMinutes"] = "60"
            })
            .Build();

        var stateStore = new RedisOptimizerStateStore(redis, NullLogger<RedisOptimizerStateStore>.Instance, config);
        var historyStore = new InMemoryOptimizerHistoryStore();
        var service = new OptimizerSessionService(
            NullLoggerFactory.Instance,
            NullLogger<OptimizerSessionService>.Instance,
            stateStore,
            historyStore,
            config);

        var request = new CreateOptimizerSessionRequest(
            N: 2,
            Sperrzeit1: 60,
            Sperrzeit2: 120,
            UseOrTools: true,
            UseGreedyFallback: false);

        var createdSessionId = service.Create(request);
        Assert.NotEqual(Guid.Empty, createdSessionId);

        var redisDb = redis.GetDatabase();
        var redisKey = RedisKeyPrefix + createdSessionId.ToString("D");
        Assert.True(redisDb.KeyExists(redisKey));

        var session = historyStore.TryGetSession(createdSessionId);
        Assert.NotNull(session);
        Assert.Equal(2, session!.Config.N);
        Assert.Equal(60, session.Config.Sperrzeit1);
        Assert.Equal(120, session.Config.Sperrzeit2);
        Assert.True(session.Config.UseOrTools);
        Assert.False(session.Config.UseGreedyFallback);
        Assert.Null(session.EndedAtUtc);

        var createdRequests = historyStore.GetRequests(createdSessionId);
        Assert.Single(createdRequests);
        Assert.Equal("session_created", createdRequests[0].RequestType);

        var deleted = service.Delete(createdSessionId);
        Assert.True(deleted);
        Assert.False(redisDb.KeyExists(redisKey));

        var allRequests = historyStore.GetRequests(createdSessionId);
        Assert.Equal(2, allRequests.Count);
        Assert.Equal("session_created", allRequests[0].RequestType);
        Assert.Equal("session_deleted", allRequests[1].RequestType);

        var endedSession = historyStore.TryGetSession(createdSessionId);
        Assert.NotNull(endedSession?.EndedAtUtc);
    }

    private static IConnectionMultiplexer? TryConnectRedis(string connectionString)
    {
        try
        {
            return ConnectionMultiplexer.Connect(connectionString);
        }
        catch
        {
            return null;
        }
    }
}
