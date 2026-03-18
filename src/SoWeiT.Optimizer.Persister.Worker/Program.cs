using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using SoWeiT.Optimizer.Persister.Worker.Configuration;
using SoWeiT.Optimizer.Persistence.History.Data;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persister.Worker;

var builder = Host.CreateApplicationBuilder(args);
Log.Logger = CreateConsoleLogger();

var consulLoadResult = builder.Configuration.AddConsulConfiguration(builder.Environment);

ReportConsulLoadIssues(consulLoadResult);
ValidateRequiredConfiguration(
    builder.Configuration,
    [
        "ConnectionStrings:Postgres",
        "RabbitMq:HostName",
        "RabbitMq:Port",
        "RabbitMq:UserName",
        "RabbitMq:Password",
        "RabbitMq:VirtualHost",
        "RabbitMq:QueueName",
        "RabbitMq:MaxRetryCount"
    ]);

builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    ConfigureSerilog(loggerConfiguration, services, builder.Configuration, consulLoadResult);
});

builder.Services.AddDbContextFactory<OptimizerHistoryDbContext>(options =>
{
    var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
                                   ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");
    options.UseNpgsql(postgresConnectionString);
});
builder.Services.AddSingleton<IOptimizerUnitOfWorkFactory, EfCoreOptimizerUnitOfWorkFactory>();
builder.Services.AddSingleton<IOptimizerHistoryStore, EfCoreOptimizerHistoryStore>();
builder.Services.AddSingleton<RabbitMqHistoryConsumer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqHistoryConsumer>());

var host = builder.Build();
var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

using (var scope = host.Services.CreateScope())
{
    try
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OptimizerHistoryDbContext>>();
        using var dbContext = dbFactory.CreateDbContext();
        startupLogger.LogInformation("Applying database migrations");
        dbContext.Database.Migrate();
        startupLogger.LogInformation("Database migrations applied");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(
            ex,
            "Database is currently not reachable. Worker continues running and retries persistence when messages are processed.");
    }
}

startupLogger.LogInformation("Starting host");
host.Run();

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
