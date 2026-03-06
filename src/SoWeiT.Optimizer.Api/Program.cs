using Microsoft.EntityFrameworkCore;
using Serilog;
using SoWeiT.Optimizer.Persistence.History.Data;
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
builder.Services.AddDbContextFactory<OptimizerHistoryDbContext>(options =>
{
    var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
                                   ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");
    options.UseNpgsql(postgresConnectionString);
});
builder.Services.AddSingleton<IOptimizerUnitOfWorkFactory, EfCoreOptimizerUnitOfWorkFactory>();
builder.Services.AddSingleton<IOptimizerHistoryStore, EfCoreOptimizerHistoryStore>();

var app = builder.Build();

app.UseSerilogRequestLogging();


    app.UseSwagger();
    app.UseSwaggerUI();


app.UseHttpsRedirection();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OptimizerHistoryDbContext>>();
    using var dbContext = dbFactory.CreateDbContext();
    dbContext.Database.Migrate();
}

app.Run();

