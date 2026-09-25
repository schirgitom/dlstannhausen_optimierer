namespace SoWeiT.Optimizer.Messaging.RabbitMq;

public sealed class RabbitMqHistoryOptions
{
    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    public string QueueName { get; set; } = "optimizer.history";

    public int MaxRetryCount { get; set; } = 5;

    public int ConnectTimeoutMilliseconds { get; set; } = 1000;

    public int PublishQueueCapacity { get; set; } = 1024;
}
