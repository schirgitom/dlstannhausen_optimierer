using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SoWeiT.Optimizer.Service.Contracts;
using SoWeiT.Optimizer.Api.Controllers;
using SoWeiT.Optimizer.Persistence.History.Data;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persistence.Redis.Persistence;
using SoWeiT.Optimizer.Service.Services;
using StackExchange.Redis;
using System.Globalization;
using Xunit.Sdk;

namespace SoWeiT.Optimizer.Tests;

public sealed class OptimizerControllerCsvEndToEndTests
{
    private const string RedisKeyPrefix = "optimizer:sessions:";

    [Fact]
    public void Controller_EndToEnd_ReplaysAllCsvRows_AndPersistsToPostgres()
    {
        var csvPath = Path.Combine(AppContext.BaseDirectory, "Testdaten.csv");
        Assert.True(File.Exists(csvPath), $"CSV not found at '{csvPath}'.");

        var lines = File.ReadLines(csvPath)
            .Skip(1)
            .Where(static l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.NotEmpty(lines);

        var redisConnectionString = Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION")
                                    ?? "localhost:6379";
        var postgresConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")
                                       ?? "Host=localhost;Port=5433;Database=soweit_optimizer;Username=dlstannhausen;Password=dlstannhausen";

        using var redis = ConnectRedisOrThrow(redisConnectionString);
        using var db = ConnectPostgresOrThrow(postgresConnectionString);

        const int userCount = 7;
        var pvVerbrauchEnergieStand = new double[userCount];
        var verbrauchEnergieStand = new double[userCount];
        var step = TimeSpan.FromSeconds(15);
        var startZeitstempel = DateTimeOffset.UtcNow;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OptimizerStateStore:SessionTtlMinutes"] = "60"
            })
            .Build();

        var stateStore = new RedisOptimizerStateStore(redis, NullLogger<RedisOptimizerStateStore>.Instance, config);
        var historyStore = new EfCoreOptimizerHistoryStore(
            new SingleUnitOfWorkFactory(postgresConnectionString),
            NullLogger<EfCoreOptimizerHistoryStore>.Instance);
        var service = new OptimizerSessionService(
            NullLoggerFactory.Instance,
            NullLogger<OptimizerSessionService>.Instance,
            stateStore,
            historyStore,
            config);
        var controller = BuildController(service);

        Guid sessionId = Guid.Empty;
        var redisDb = redis.GetDatabase();
        try
        {
            var createResult = controller.Create(new CreateOptimizerSessionRequest(
                N: userCount,
                Sperrzeit1: 5 * 60,
                Sperrzeit2: 60,
                UseOrTools: true,
                UseGreedyFallback: false));

            var createOk = Assert.IsType<OkObjectResult>(createResult.Result);
            var createResponse = Assert.IsType<CreateOptimizerSessionResponse>(createOk.Value);
            sessionId = createResponse.SessionId;
            Assert.NotEqual(Guid.Empty, sessionId);
            Assert.True(redisDb.KeyExists(RedisKeyPrefix + sessionId.ToString("D")));

            var rowCount = 0;
            foreach (var line in lines)
            {
                var parts = line.Split(';');
                Assert.Equal(8, parts.Length);

                var verbrauch = new double[userCount];
                for (var i = 0; i < userCount; i++)
                {
                    verbrauch[i] = Parse(parts[i]);
                    Assert.True(verbrauch[i] >= 0.0, $"Verbrauch muss >= 0 sein (row {rowCount + 1}, user {i + 1}).");
                }

                var pv = Parse(parts[7]);
                Assert.True(pv >= 0.0, $"PV muss >= 0 sein (row {rowCount + 1}).");
                var nutzer = BuildNutzer(verbrauch);

                var zeitstempel = startZeitstempel.AddSeconds(rowCount * step.TotalSeconds);
                var runResult = controller.Run(
                    new RunRequest(
                        pv,
                        nutzer,
                        zeitstempel,
                        pvVerbrauchEnergieStand,
                        verbrauchEnergieStand),
                    sessionId);
                var runOk = Assert.IsType<OkObjectResult>(runResult.Result);
                var runResponse = Assert.IsType<RunResponse>(runOk.Value);

                Assert.Equal(userCount, runResponse.Schaltzustand.Length);
                Assert.Equal(userCount, runResponse.ResOpt.Length);

                var assigned = 0.0;
                for (var i = 0; i < userCount; i++)
                {
                    Assert.True(runResponse.Schaltzustand[i] is 0.0 or 1.0, $"Schaltzustand muss 0 oder 1 sein (row {rowCount + 1}, user {i + 1}).");
                    Assert.True(runResponse.ResOpt[i] >= -1e-9, $"ResOpt darf nicht negativ sein (row {rowCount + 1}, user {i + 1}).");
                    Assert.True(runResponse.ResOpt[i] <= verbrauch[i] + 1e-9, $"ResOpt darf Verbrauch nicht ueberschreiten (row {rowCount + 1}, user {i + 1}).");
                    assigned += runResponse.ResOpt[i];
                    pvVerbrauchEnergieStand[i] += runResponse.ResOpt[i];
                    verbrauchEnergieStand[i] += verbrauch[i];
                }

                Assert.True(assigned <= pv + 1e-6, $"Summierte Zuweisung darf PV nicht ueberschreiten (row {rowCount + 1}).");

                rowCount++;
            }

            Assert.Equal(lines.Length, rowCount);

            var runRequests = db.Requests
                .AsNoTracking()
                .Where(x => x.SessionId == sessionId && x.RequestType == "run")
                .ToList();
            Assert.Empty(runRequests);

            var requestIds = db.Requests
                .AsNoTracking()
                .Where(x => x.SessionId == sessionId && x.RequestType == "run")
                .Select(x => x.Id)
                .ToArray();
            var userLogs = db.RequestUsers
                .AsNoTracking()
                .Include(x => x.Customer)
                .Where(x => requestIds.Contains(x.RequestEntryId))
                .ToList();
            Assert.Empty(userLogs);

            var deleteResult = controller.Delete(sessionId);
            Assert.IsType<NoContentResult>(deleteResult);
            Assert.False(redisDb.KeyExists(RedisKeyPrefix + sessionId.ToString("D")));

            var endedSession = db.Sessions
                .AsNoTracking()
                .Single(x => x.SessionId == sessionId);
            Assert.NotNull(endedSession.EndedAtUtc);
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                redisDb.KeyDelete(RedisKeyPrefix + sessionId.ToString("D"));

                var requestIds = db.Requests
                    .Where(x => x.SessionId == sessionId)
                    .Select(x => x.Id)
                    .ToArray();
                if (requestIds.Length > 0)
                {
                    db.RequestUsers
                        .Where(x => requestIds.Contains(x.RequestEntryId))
                        .ExecuteDelete();
                }

                db.Requests
                    .Where(x => x.SessionId == sessionId)
                    .ExecuteDelete();
                db.Sessions
                    .Where(x => x.SessionId == sessionId)
                    .ExecuteDelete();
            }
        }
    }

    private static OptimizerSessionsController BuildController(OptimizerSessionService service)
    {
        var controller = new OptimizerSessionsController(
            service,
            NullLogger<OptimizerSessionsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static double Parse(string value)
    {
        return double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private static UserPowerRequest[] BuildNutzer(double[] verbrauch)
    {
        var users = new UserPowerRequest[verbrauch.Length];
        for (var i = 0; i < verbrauch.Length; i++)
        {
            users[i] = new UserPowerRequest(BuildCustomerNumber(i), verbrauch[i]);
        }

        return users;
    }

    private static string BuildCustomerNumber(int index)
    {
        return $"K-{index + 1:000}";
    }

    private static IConnectionMultiplexer ConnectRedisOrThrow(string connectionString)
    {
        try
        {
            return ConnectionMultiplexer.Connect(connectionString);
        }
        catch (Exception ex)
        {
            throw new XunitException($"Redis connection failed for '{connectionString}': {ex.Message}");
        }
    }

    private static OptimizerHistoryDbContext ConnectPostgresOrThrow(string connectionString)
    {
        try
        {
            var options = new DbContextOptionsBuilder<OptimizerHistoryDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            var db = new OptimizerHistoryDbContext(options);
            db.Database.Migrate();
            db.Database.OpenConnection();
            db.Database.CloseConnection();
            return db;
        }
        catch (Exception ex)
        {
            throw new XunitException($"Postgres connection/schema setup failed for '{connectionString}': {ex.Message}");
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

