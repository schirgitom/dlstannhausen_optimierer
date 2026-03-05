using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SoWeiT.Optimizer.Api.Contracts;
using SoWeiT.Optimizer.Api.Data;
using SoWeiT.Optimizer.Api.Persistence;
using SoWeiT.Optimizer.Api.Services;
using System.Globalization;
using StackExchange.Redis;

namespace SoWeiT.Optimizer.Tests;

public sealed class OptimiererCsvPythonParameterTests
{
    private const string RedisKeyPrefix = "optimizer:sessions:";

    [Fact]
    public void Run_MitTestdatenCsv_UndPythonParametern_EndToEndMitPersistenz()
    {
        const int n = 7;
        const int simDauer = 5760 * 2;
        const int sperrzeit1 = 5 * 60;
        const int sperrzeit2 = 60;

        var csvPath = Path.Combine(AppContext.BaseDirectory, "Testdaten.csv");
        Assert.True(File.Exists(csvPath), $"CSV not found at '{csvPath}'.");

        var lines = File.ReadLines(csvPath)
            .Skip(1)
            .Where(static l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.True(lines.Length >= simDauer, $"CSV must have at least {simDauer} data rows.");

        var redisConnectionString = Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION")
                                    ?? "localhost:6379";
        var postgresConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")
                                       ?? "Host=localhost;Port=5433;Database=soweit_optimizer;Username=dlstannhausen;Password=dlstannhausen";

        using var redis = TryConnectRedis(redisConnectionString);
        Assert.NotNull(redis);

        using var db = TryConnectPostgres(postgresConnectionString);
        Assert.NotNull(db);

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

        var createRequest = new CreateOptimizerSessionRequest(
            N: n,
            Sperrzeit1: sperrzeit1,
            Sperrzeit2: sperrzeit2,
            UseOrTools: true,
            UseGreedyFallback: false);

        Guid sessionId = Guid.Empty;
        var redisDb = redis.GetDatabase();
        var schaltzustaende = new double[simDauer, n];
        var pvVerbrauchEnergieStand = new double[n];
        var verbrauchEnergieStand = new double[n];
        var resOpt = new double[n];
        var zeitstempel = new DateTimeOffset(2021, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var step = TimeSpan.FromSeconds(15);
        double lastPv = 0.0;

        try
        {
            sessionId = service.Create(createRequest);
            var redisKey = RedisKeyPrefix + sessionId.ToString("D");
            Assert.True(redisDb.KeyExists(redisKey));

            Assert.True(service.TryGet(sessionId, out var opt1));
            Assert.NotNull(opt1);

            opt1!.UpdateVerteilungMittelsEnergie(pvVerbrauchEnergieStand, verbrauchEnergieStand);
            service.PersistMutation(
                sessionId,
                opt1,
                "update_verteilung_mittels_energie",
                DateTimeOffset.UtcNow,
                opt1.Erzeugung);

            for (var j = 0; j < simDauer; j++)
            {
                var parts = lines[j].Split(';');
                Assert.Equal(8, parts.Length);

                var verbrauch = new double[n];
                for (var i = 0; i < n; i++)
                {
                    verbrauch[i] = Parse(parts[i]);
                }

                var pv = Parse(parts[7]);
                lastPv = pv;

                zeitstempel = zeitstempel.Add(step);
                var result = opt1.Run(pv, verbrauch, zeitstempel);
                service.PersistMutation(
                    sessionId,
                    opt1,
                    "run",
                    zeitstempel,
                    pv,
                    BuildRunUserLogs(verbrauch, result.Schaltzustand));

                for (var i = 0; i < n; i++)
                {
                    schaltzustaende[j, i] = result.Schaltzustand[i];
                    resOpt[i] = result.ResOpt[i];
                    pvVerbrauchEnergieStand[i] += resOpt[i];
                    verbrauchEnergieStand[i] += verbrauch[i];
                }

                opt1.UpdateVerteilungMittelsEnergie(pvVerbrauchEnergieStand, verbrauchEnergieStand);
                service.PersistMutation(
                    sessionId,
                    opt1,
                    "update_verteilung_mittels_energie",
                    DateTimeOffset.UtcNow,
                    opt1.Erzeugung);
            }

            Assert.True(stateStore.TryLoad(sessionId, out var persisted));
            Assert.NotNull(persisted);
            Assert.Equal(n, persisted!.Snapshot.N);
            Assert.Equal(sperrzeit1, persisted.Snapshot.Sperrzeit1);
            Assert.Equal(sperrzeit2, persisted.Snapshot.Sperrzeit2);
            Assert.NotNull(persisted.Snapshot.PvVerbrauchEnergieStand);
            Assert.NotNull(persisted.Snapshot.VerbrauchEnergieStand);
            Assert.Equal(pvVerbrauchEnergieStand.Length, persisted.Snapshot.PvVerbrauchEnergieStand!.Length);
            Assert.Equal(verbrauchEnergieStand.Length, persisted.Snapshot.VerbrauchEnergieStand!.Length);

            for (var i = 0; i < n; i++)
            {
                Assert.Equal(pvVerbrauchEnergieStand[i], persisted.Snapshot.PvVerbrauchEnergieStand[i], 9);
                Assert.Equal(verbrauchEnergieStand[i], persisted.Snapshot.VerbrauchEnergieStand[i], 9);
            }

            var runRequests = db.Requests
                .AsNoTracking()
                .Where(x => x.SessionId == sessionId && x.RequestType == "run")
                .OrderBy(x => x.Id)
                .ToList();
            Assert.Equal(simDauer, runRequests.Count);
            Assert.Equal(lastPv, runRequests[^1].AvailablePvPowerWatt.GetValueOrDefault(), 9);

            var allRequestIds = db.Requests
                .Where(x => x.SessionId == sessionId)
                .Select(x => x.Id)
                .ToArray();
            var userEntries = db.RequestUsers
                .AsNoTracking()
                .Where(x => allRequestIds.Contains(x.RequestEntryId))
                .ToList();
            Assert.Equal(simDauer * n, userEntries.Count);

            var deleted = service.Delete(sessionId);
            Assert.True(deleted);
            Assert.False(redisDb.KeyExists(redisKey));

            var endedSession = db.Sessions
                .AsNoTracking()
                .Single(x => x.SessionId == sessionId);
            Assert.NotNull(endedSession.EndedAtUtc);
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                var key = RedisKeyPrefix + sessionId.ToString("D");
                redisDb.KeyDelete(key);

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

        Assert.Equal(simDauer, schaltzustaende.GetLength(0));
        Assert.Equal(n, schaltzustaende.GetLength(1));
        Assert.All(pvVerbrauchEnergieStand, static v => Assert.True(v >= -1e-9));
        Assert.All(verbrauchEnergieStand, static v => Assert.True(v >= -1e-9));
    }

    private static double Parse(string value)
    {
        return double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<OptimizerRequestUserLog> BuildRunUserLogs(double[] requiredPowerWatt, double[] switchState)
    {
        var users = new List<OptimizerRequestUserLog>(requiredPowerWatt.Length);
        for (var i = 0; i < requiredPowerWatt.Length; i++)
        {
            users.Add(new OptimizerRequestUserLog(i, requiredPowerWatt[i], switchState[i] > 0.0));
        }

        return users;
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
