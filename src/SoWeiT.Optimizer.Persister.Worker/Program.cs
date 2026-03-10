using Microsoft.EntityFrameworkCore;
using SoWeiT.Optimizer.Persistence.History.Data;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persister.Worker;

var builder = Host.CreateApplicationBuilder(args);

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

using (var scope = host.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OptimizerHistoryDbContext>>();
    using var dbContext = dbFactory.CreateDbContext();
    dbContext.Database.Migrate();
}

host.Run();
