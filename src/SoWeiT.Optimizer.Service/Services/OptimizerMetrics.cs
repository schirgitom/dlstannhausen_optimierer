using Prometheus;

namespace SoWeiT.Optimizer.Service.Services;

/// <summary>
/// Prometheus metrics for the optimizer service.
/// All metrics are registered on the default registry and exposed at /metrics.
/// </summary>
public sealed class OptimizerMetrics
{
    private static readonly TimeSpan CustomerOfflineAfter = TimeSpan.FromMinutes(5);

    // Session lifecycle counters
    public readonly Counter SessionsCreatedTotal = Metrics.CreateCounter(
        "optimizer_sessions_created_total",
        "Total number of optimizer sessions created.");

    public readonly Counter SessionsDeletedTotal = Metrics.CreateCounter(
        "optimizer_sessions_deleted_total",
        "Total number of optimizer sessions deleted.");

    public readonly Counter SessionsExpiredTotal = Metrics.CreateCounter(
        "optimizer_sessions_expired_total",
        "Total number of optimizer sessions expired due to inactivity.");

    // Active sessions gauge
    public readonly Gauge ActiveSessionsCount = Metrics.CreateGauge(
        "optimizer_sessions_active",
        "Current number of active optimizer sessions in memory.");

    // Operation counters
    public readonly Counter RunRequestsTotal = Metrics.CreateCounter(
        "optimizer_run_requests_total",
        "Total number of run requests processed.");

    public readonly Counter PreprocessingRequestsTotal = Metrics.CreateCounter(
        "optimizer_preprocessing_requests_total",
        "Total number of preprocessing requests processed.");

    public readonly Counter PostprocessingRequestsTotal = Metrics.CreateCounter(
        "optimizer_postprocessing_requests_total",
        "Total number of postprocessing requests processed.");

    public readonly Counter RunRecoveriesTotal = Metrics.CreateCounter(
        "optimizer_run_recoveries_total",
        "Total number of run requests that triggered automatic session recovery.");

    // Per-customer: seconds since last data was received
    public readonly Gauge CustomerLastDataAgeSeconds = Metrics.CreateGauge(
        "optimizer_customer_last_data_age_seconds",
        "Seconds since the customer last sent data (run or preprocessing request).",
        labelNames: ["customer"]);

    // Per-customer: Unix timestamp of last data received
    public readonly Gauge CustomerLastDataTimestampSeconds = Metrics.CreateGauge(
        "optimizer_customer_last_data_timestamp_seconds",
        "Unix timestamp (UTC) of the last data received from the customer.",
        labelNames: ["customer"]);

    // Per-customer availability state
    public readonly Gauge CustomerOnline = Metrics.CreateGauge(
        "optimizer_customer_online",
        "Whether the customer station is online (1) or offline (0); offline after 5 minutes without data.",
        labelNames: ["customer"]);

    public readonly Gauge CustomerOnlineSinceTimestampSeconds = Metrics.CreateGauge(
        "optimizer_customer_online_since_timestamp_seconds",
        "Unix timestamp (UTC) when the customer station most recently became online.",
        labelNames: ["customer"]);

    public readonly Gauge CustomerLastOfflineTimestampSeconds = Metrics.CreateGauge(
        "optimizer_customer_last_offline_timestamp_seconds",
        "Unix timestamp (UTC) when the customer station most recently went offline.",
        labelNames: ["customer"]);

    // PV power gauge (last known value per session)
    public readonly Gauge PvPowerWatt = Metrics.CreateGauge(
        "optimizer_pv_power_watt",
        "Last reported PV generation power in watts.",
        labelNames: ["session_id"]);

    // Per-customer power consumption
    public readonly Gauge CustomerPowerWatt = Metrics.CreateGauge(
        "optimizer_customer_power_watt",
        "Last reported power consumption in watts per customer.",
        labelNames: ["customer"]);

    // Run duration histogram
    public readonly Histogram RunDurationSeconds = Metrics.CreateHistogram(
        "optimizer_run_duration_seconds",
        "Duration of optimizer run calls in seconds.",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 12)
        });

    // Tracks last-seen timestamps per customer (used to compute age)
    private readonly Dictionary<string, DateTimeOffset> _customerLastSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _customerOnline = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Records that a customer sent data and updates the age/timestamp gauges.
    /// </summary>
    public void RecordCustomerData(string customer, double powerWatt, DateTimeOffset requestTimestamp)
    {
        lock (_lock)
        {
            _customerLastSeen[customer] = requestTimestamp;

            if (!_customerOnline.TryGetValue(customer, out var wasOnline) || !wasOnline)
            {
                CustomerOnlineSinceTimestampSeconds.WithLabels(customer).Set(requestTimestamp.ToUnixTimeSeconds());
            }

            _customerOnline[customer] = true;
            CustomerOnline.WithLabels(customer).Set(1);
        }

        var unixTs = requestTimestamp.ToUnixTimeSeconds();
        CustomerLastDataTimestampSeconds.WithLabels(customer).Set(unixTs);
        CustomerPowerWatt.WithLabels(customer).Set(powerWatt);
        // Age is 0 at the moment of recording; the scrape callback updates it continuously.
        CustomerLastDataAgeSeconds.WithLabels(customer).Set(0);
    }

    /// <summary>
    /// Refreshes the age gauges for all known customers. Call this before each Prometheus scrape
    /// or periodically from a background service.
    /// </summary>
    public void RefreshCustomerAges()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            foreach (var (customer, lastSeen) in _customerLastSeen)
            {
                var age = now - lastSeen;
                CustomerLastDataAgeSeconds.WithLabels(customer).Set(age.TotalSeconds);

                if (age >= CustomerOfflineAfter && _customerOnline[customer])
                {
                    _customerOnline[customer] = false;
                    CustomerOnline.WithLabels(customer).Set(0);
                    CustomerLastOfflineTimestampSeconds.WithLabels(customer)
                        .Set((lastSeen + CustomerOfflineAfter).ToUnixTimeSeconds());
                }
            }
        }
    }
}
