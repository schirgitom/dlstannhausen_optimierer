using SoWeiT.Optimizer.Api.Configuration;
using Prometheus;
using Serilog;
using SoWeiT.Optimizer.Messaging.RabbitMq;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persistence.Redis.Persistence;
using SoWeiT.Optimizer.Service.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
Log.Logger = CreateConsoleLogger();

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

var consulLoadResult = builder.Configuration.AddConsulConfiguration(builder.Environment);
builder.Configuration.AddEnvironmentVariables();
if (args is { Length: > 0 })
{
    builder.Configuration.AddCommandLine(args);
}

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    ConfigureSerilog(loggerConfiguration, services, context.Configuration, consulLoadResult);
});

ReportConsulLoadIssues(consulLoadResult);
ValidateRequiredConfiguration(
    builder.Configuration,
    [
        "ConnectionStrings:Redis",
        "RabbitMq:HostName",
        "RabbitMq:Port",
        "RabbitMq:UserName",
        "RabbitMq:Password",
        "RabbitMq:VirtualHost",
        "RabbitMq:QueueName",
        "OptimizerStateStore:SessionTtlMinutes",
        "OptimizerSessionRecovery:Sperrzeit1",
        "OptimizerSessionRecovery:Sperrzeit2",
        "OptimizerSessionRecovery:UseOrTools",
        "OptimizerSessionRecovery:UseGreedyFallback",
        "AllowedHosts"
    ]);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<OptimizerMetrics>();
builder.Services.AddSingleton<OptimizerSessionService>();
builder.Services.AddHostedService<MetricsRefreshService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Redis");

    var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
                                ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.");

    ConfigurationOptions options;
    try
    {
        options = ConfigurationOptions.Parse(redisConnectionString);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Redis connection string konnte nicht geparst werden: {ConnectionString}",
            MaskRedisConnectionString(redisConnectionString));
        throw;
    }

    options.AbortOnConnectFail = false;

    var endpoints = string.Join(", ", options.EndPoints.Select(e => e.ToString()));
    logger.LogInformation(
        "Redis Verbindung wird aufgebaut. Endpoints=[{Endpoints}] Ssl={Ssl} AbortOnConnectFail={AbortOnConnectFail} " +
        "ConnectTimeout={ConnectTimeout}ms SyncTimeout={SyncTimeout}ms DefaultDatabase={DefaultDatabase} " +
        "User={User} PasswordSet={PasswordSet} ClientName={ClientName} RawConfig={RawConfig}",
        endpoints,
        options.Ssl,
        options.AbortOnConnectFail,
        options.ConnectTimeout,
        options.SyncTimeout,
        options.DefaultDatabase,
        options.User ?? "(none)",
        !string.IsNullOrEmpty(options.Password),
        options.ClientName ?? "(none)",
        MaskRedisConnectionString(redisConnectionString));

    var logWriter = new StringWriter();
    ConnectionMultiplexer multiplexer;
    try
    {
        multiplexer = ConnectionMultiplexer.Connect(options, logWriter);
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Redis Verbindung fehlgeschlagen. Endpoints=[{Endpoints}] StackExchangeLog={Log}",
            endpoints, logWriter.ToString());
        throw;
    }

    logger.LogInformation(
        "Redis Verbindung hergestellt. IsConnected={IsConnected} ConnectedEndpoints=[{Connected}] StackExchangeLog={Log}",
        multiplexer.IsConnected,
        string.Join(", ", multiplexer.GetEndPoints().Select(e => $"{e}({(multiplexer.GetServer(e).IsConnected ? "up" : "down")})")),
        logWriter.ToString());

    multiplexer.ConnectionFailed += (_, e) => logger.LogError(e.Exception,
        "Redis ConnectionFailed: EndPoint={EndPoint} ConnectionType={ConnectionType} FailureType={FailureType}",
        e.EndPoint, e.ConnectionType, e.FailureType);
    multiplexer.ConnectionRestored += (_, e) => logger.LogInformation(
        "Redis ConnectionRestored: EndPoint={EndPoint} ConnectionType={ConnectionType}",
        e.EndPoint, e.ConnectionType);
    multiplexer.InternalError += (_, e) => logger.LogError(e.Exception,
        "Redis InternalError: EndPoint={EndPoint} Origin={Origin}", e.EndPoint, e.Origin);
    multiplexer.ErrorMessage += (_, e) => logger.LogWarning(
        "Redis ErrorMessage: EndPoint={EndPoint} Message={Message}", e.EndPoint, e.Message);
    multiplexer.ConfigurationChanged += (_, e) => logger.LogInformation(
        "Redis ConfigurationChanged: EndPoint={EndPoint}", e.EndPoint);

    return multiplexer;
});
builder.Services.AddSingleton<IOptimizerStateStore, RedisOptimizerStateStore>();
builder.Services.AddSingleton<IOptimizerHistoryStore, RabbitMqOptimizerHistoryStore>();

var app = builder.Build();

app.UseSerilogRequestLogging();


    app.UseSwagger();
    app.UseSwaggerUI();


app.UseHttpsRedirection();
app.MapControllers();
app.MapMetrics();

app.Run();

static string MaskRedisConnectionString(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return connectionString;
    }

    var parts = connectionString.Split(',');
    for (var i = 0; i < parts.Length; i++)
    {
        var part = parts[i];
        var eq = part.IndexOf('=');
        if (eq <= 0)
        {
            continue;
        }

        var key = part.Substring(0, eq).Trim();
        if (key.Equals("password", StringComparison.OrdinalIgnoreCase))
        {
            parts[i] = key + "=***";
        }
    }

    return string.Join(",", parts);
}

static Serilog.ILogger CreateConsoleLogger()
{
    return new LoggerConfiguration()
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
}

static void ConfigureSerilog(
    LoggerConfiguration loggerConfiguration,
    IServiceProvider services,
    IConfiguration configuration,
    ConsulConfigurationExtensions.ConsulLoadResult consulLoadResult)
{
    loggerConfiguration
        .MinimumLevel.Information()
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");

    ConfigureOptionalSeqSink(loggerConfiguration, configuration);

    if (consulLoadResult.HasFailures)
    {
        return;
    }

    var serilogSection = configuration.GetSection("Serilog");
    if (!serilogSection.Exists())
    {
        return;
    }

    try
    {
        loggerConfiguration.ReadFrom.Configuration(configuration);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Serilog configuration from Consul/local config is invalid. Keeping console + optional Seq defaults.");
    }
}

static void ConfigureOptionalSeqSink(LoggerConfiguration loggerConfiguration, IConfiguration configuration)
{
    var seqServerUrl = configuration["Seq:ServerUrl"];
    if (string.IsNullOrWhiteSpace(seqServerUrl))
    {
        return;
    }

    var seqApiKey = configuration["Seq:ApiKey"];
    loggerConfiguration.WriteTo.Seq(
        serverUrl: seqServerUrl,
        apiKey: string.IsNullOrWhiteSpace(seqApiKey) ? null : seqApiKey);
}

static void ReportConsulLoadIssues(ConsulConfigurationExtensions.ConsulLoadResult consulLoadResult)
{
    if (!consulLoadResult.Enabled)
    {
        Log.Information(
            "Consul configuration is disabled. Using appsettings and environment variables only.");
        return;
    }

    if (!consulLoadResult.HasFailures)
    {
        Log.Information(
            "Loaded application configuration from Consul {ConsulAddress} using keys: {ConsulKeys}",
            consulLoadResult.Address,
            string.Join(", ", consulLoadResult.Keys));
        return;
    }

    foreach (var failure in consulLoadResult.Failures)
    {
        Log.Warning(
            failure.Exception,
            "Could not load Consul key {ConsulKey} from {ConsulAddress}. Console logging fallback remains active.",
            failure.Key,
            consulLoadResult.Address);
    }
}

static void ValidateRequiredConfiguration(IConfiguration configuration, IEnumerable<string> requiredKeys)
{
    var missingKeys = requiredKeys
        .Where(key => string.IsNullOrWhiteSpace(configuration[key]) && !configuration.GetSection(key).Exists())
        .Distinct()
        .ToArray();

    if (missingKeys.Length == 0)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Missing required configuration values: {string.Join(", ", missingKeys)}");
}

