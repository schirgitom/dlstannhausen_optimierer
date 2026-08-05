using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SoWeiT.Optimizer.Messaging.RabbitMq;
using SoWeiT.Optimizer.Persistence.History.Persistence;

namespace SoWeiT.Optimizer.Tests;

public sealed class RabbitMqOptimizerHistoryStoreTests
{
    [Fact]
    public void CreateSession_DoesNotThrow_WhenRabbitMqIsUnavailable()
    {
        var store = CreateStore();

        var exception = Record.Exception(() =>
            store.CreateSession(
                Guid.NewGuid(),
                new OptimizerSessionConfig(2, 60, 120, true, false),
                DateTime.UtcNow));

        Assert.Null(exception);
    }

    [Fact]
    public void MarkSessionEnded_DoesNotThrow_WhenRabbitMqIsUnavailable()
    {
        var store = CreateStore();

        var exception = Record.Exception(() => store.MarkSessionEnded(Guid.NewGuid(), DateTime.UtcNow));

        Assert.Null(exception);
    }

    [Fact]
    public void AppendRequest_DoesNotThrow_WhenRabbitMqIsUnavailable()
    {
        var store = CreateStore();

        var exception = Record.Exception(() =>
            store.AppendRequest(
                Guid.NewGuid(),
                new OptimizerRequestLog(
                    RequestType: "run",
                    RequestTimestamp: DateTimeOffset.UtcNow,
                    AvailablePvPowerWatt: 1500,
                    Users: [])));

        Assert.Null(exception);
    }

    private static RabbitMqOptimizerHistoryStore CreateStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:HostName"] = "127.0.0.1",
                ["RabbitMq:Port"] = "1",
                ["RabbitMq:UserName"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:QueueName"] = "optimizer.history"
            })
            .Build();

        return new RabbitMqOptimizerHistoryStore(configuration, NullLogger<RabbitMqOptimizerHistoryStore>.Instance);
    }
}
