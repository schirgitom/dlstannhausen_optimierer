using Serilog;
using SoWeiT.Optimizer.Messaging.RabbitMq;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persistence.Redis.Persistence;
using SoWeiT.Optimizer.Service.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

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

