using SoWeiT.Optimizer.Api.Configuration;
using Serilog;
using SoWeiT.Optimizer.Messaging.RabbitMq;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persistence.Redis.Persistence;
using SoWeiT.Optimizer.Service.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
Log.Logger = CreateConsoleLogger();

var consulLoadResult = builder.Configuration.AddConsulConfiguration(builder.Environment);

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
builder.Services.AddSingleton<OptimizerSessionService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
                                ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.");
    return ConnectionMultiplexer.Connect(redisConnectionString);
});
builder.Services.AddSingleton<IOptimizerStateStore, RedisOptimizerStateStore>();
builder.Services.AddSingleton<IOptimizerHistoryStore, RabbitMqOptimizerHistoryStore>();

var app = builder.Build();

app.UseSerilogRequestLogging();


    app.UseSwagger();
    app.UseSwaggerUI();


app.UseHttpsRedirection();
app.MapControllers();

app.Run();

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

