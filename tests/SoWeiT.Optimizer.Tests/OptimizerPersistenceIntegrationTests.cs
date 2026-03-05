using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SoWeiT.Optimizer.Api.Contracts;
using SoWeiT.Optimizer.Api.Data;
using SoWeiT.Optimizer.Api.Persistence;
using SoWeiT.Optimizer.Api.Services;
using StackExchange.Redis;

namespace SoWeiT.Optimizer.Tests;

public sealed class OptimizerPersistenceIntegrationTests
{
    private const string RedisKeyPrefix = "optimizer:sessions:";

    [Fact]
    public void SessionLifecycle_PersistsInRedisAndPostgres()
    {
        var redisConnectionString = Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION")
                                    ?? "localhost:6379";
        var postgresConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")
                                       ?? "Host=localhost;Port=5433;Database=soweit_optimizer;Username=dlstannhausen;Password=dlstannhausen";

        using var redis = TryConnectRedis(redisConnectionString);
        if (redis is null)
        {
            return;
        }

        using var db = TryConnectPostgres(postgresConnectionString);
        if (db is null)
        {
            return;
        }

        var sessionId = Guid.NewGuid();
        var redisDb = redis.GetDatabase();
        var key = RedisKeyPrefix + sessionId.ToString("D");

        redisDb.KeyDelete(key);
        var seedRequestIds = db.Requests
            .Where(x => x.SessionId == sessionId)
            .Select(x => x.Id)
            .ToArray();
        if (seedRequestIds.Length > 0)
        {
            db.RequestUsers
                .Where(x => seedRequestIds.Contains(x.RequestEntryId))
                .ExecuteDelete();
        }
        db.Requests
            .Where(x => x.SessionId == sessionId)
            .ExecuteDelete();
        db.Sessions
            .Where(x => x.SessionId == sessionId)
            .ExecuteDelete();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OptimizerStateStore:SessionTtlMinutes"] = "60"
            })
            .Build();

        var stateStore = new RedisOptimizerStateStore(redis, NullLogger<RedisOptimizerStateStore>.Instance, config);
        var historyStore = new EfCoreOptimizerHistoryStore(new SingleUnitOfWorkFactory(postgresConnectionString), NullLogger<EfCoreOptimizerHistoryStore>.Instance);
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

        var redisKey = RedisKeyPrefix + createdSessionId.ToString("D");
        Assert.True(redisDb.KeyExists(redisKey));

        var createdSession = db.Sessions
            .AsNoTracking()
            .SingleOrDefault(x => x.SessionId == createdSessionId);
        Assert.NotNull(createdSession);
        Assert.Equal(2, createdSession!.N);
        Assert.Equal(60, createdSession.Sperrzeit1);
        Assert.Equal(120, createdSession.Sperrzeit2);
        Assert.True(createdSession.UseOrTools);
        Assert.False(createdSession.UseGreedyFallback);
        Assert.Null(createdSession.EndedAtUtc);

        var createdRequests = db.Requests
            .AsNoTracking()
            .Where(x => x.SessionId == createdSessionId)
            .OrderBy(x => x.Id)
            .ToList();

        Assert.Single(createdRequests);
        Assert.Equal("session_created", createdRequests[0].RequestType);
        Assert.Empty(db.RequestUsers.Where(x => x.RequestEntryId == createdRequests[0].Id));

        var deleted = service.Delete(createdSessionId);
        Assert.True(deleted);
        Assert.False(redisDb.KeyExists(redisKey));

        var allRequests = db.Requests
            .AsNoTracking()
            .Where(x => x.SessionId == createdSessionId)
            .OrderBy(x => x.Id)
            .ToList();

        Assert.Equal(2, allRequests.Count);
        Assert.Equal("session_created", allRequests[0].RequestType);
        Assert.Equal("session_deleted", allRequests[1].RequestType);

        var endedSession = db.Sessions
            .AsNoTracking()
            .Single(x => x.SessionId == createdSessionId);
        Assert.NotNull(endedSession.EndedAtUtc);

        var requestIds = db.Requests
            .Where(x => x.SessionId == createdSessionId)
            .Select(x => x.Id)
            .ToArray();
        if (requestIds.Length > 0)
        {
            db.RequestUsers
                .Where(x => requestIds.Contains(x.RequestEntryId))
                .ExecuteDelete();
        }
        db.Requests.Where(x => x.SessionId == createdSessionId).ExecuteDelete();
        db.Sessions.Where(x => x.SessionId == createdSessionId).ExecuteDelete();
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

    private static OptimizerHistoryDbContext? TryConnectPostgres(string connectionString)
    {
        try
        {
            var options = new DbContextOptionsBuilder<OptimizerHistoryDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            var db = new OptimizerHistoryDbContext(options);
            db.Database.EnsureCreated();
            db.Database.OpenConnection();
            db.Database.CloseConnection();
            return db;
        }
        catch
        {
            return null;
        }
    }

    private sealed class SingleUnitOfWorkFactory : IOptimizerUnitOfWorkFactory
    {
        private readonly string _connectionString;

        public SingleUnitOfWorkFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IOptimizerUnitOfWork Create()
        {
            var options = new DbContextOptionsBuilder<OptimizerHistoryDbContext>()
                .UseNpgsql(_connectionString)
                .Options;
            return new EfCoreOptimizerUnitOfWork(new OptimizerHistoryDbContext(options));
        }
    }
}
