using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using SoWeiT.Optimizer.Api.Configuration;
using SoWeiT.Optimizer.Persistence.History.Data;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persister.Worker;
using Winton.Extensions.Configuration.Consul;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConsulConfiguration(builder.Environment);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();
var consulAddress = builder.Configuration["Consul:Address"];
var consulKeyPrefix = builder.Configuration["Consul:KeyPrefix"] ?? "soweit/optimizer/persister";

if (!string.IsNullOrWhiteSpace(consulAddress))
{
    builder.Configuration.AddConsul(
        $"{consulKeyPrefix}/appsettings.json",
        options =>
        {
            options.Optional = true;
            options.ReloadOnChange = true;
            options.ConsulConfigurationOptions = consulOptions =>
            {
                consulOptions.Address = new Uri(consulAddress);
            };
        });

    builder.Configuration.AddConsul(
        $"{consulKeyPrefix}/appsettings.{builder.Environment.EnvironmentName}.json",
        options =>
        {
            options.Optional = true;
            options.ReloadOnChange = true;
            options.ConsulConfigurationOptions = consulOptions =>
            {
                consulOptions.Address = new Uri(consulAddress);
            };
        });
}

builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();

    try
    {
        var serilogSection = builder.Configuration.GetSection("Serilog");
        var hasSerilogSection = serilogSection.Exists();
        var hasWriteTo = serilogSection.GetSection("WriteTo").GetChildren().Any();

        if (!hasSerilogSection || !hasWriteTo)
        {
            loggerConfiguration
                .MinimumLevel.Information()
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
            return;
        }

        loggerConfiguration.ReadFrom.Configuration(builder.Configuration);
    }
    catch (Exception ex)
    {
        loggerConfiguration
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
        Console.Error.WriteLine($"Serilog configuration is invalid; using console fallback. {ex.Message}");
    }
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
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OptimizerHistoryDbContext>>();
    using var dbContext = dbFactory.CreateDbContext();
    startupLogger.LogInformation("Applying database migrations");
    dbContext.Database.Migrate();
    startupLogger.LogInformation("Database migrations applied");
}

startupLogger.LogInformation("Starting host");
host.Run();
