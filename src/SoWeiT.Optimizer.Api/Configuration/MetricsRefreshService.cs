using SoWeiT.Optimizer.Service.Services;

namespace SoWeiT.Optimizer.Api.Configuration;

/// <summary>
/// Background service that periodically refreshes time-based Prometheus gauges
/// (e.g. how many seconds ago each customer last sent data).
/// </summary>
internal sealed class MetricsRefreshService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    private readonly OptimizerMetrics _metrics;

    public MetricsRefreshService(OptimizerMetrics metrics)
    {
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            _metrics.RefreshCustomerAges();
        }
    }
}
