using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SoWeiT.Optimizer.Persistence.History.Data;

public sealed class OptimizerHistoryDbContextFactory : IDesignTimeDbContextFactory<OptimizerHistoryDbContext>
{
    public OptimizerHistoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<OptimizerHistoryDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new OptimizerHistoryDbContext(optionsBuilder.Options);
    }

    private static string ResolveConnectionString(string[] args)
    {
        var argumentConnection = GetArgumentValue(args, "--connection");
        if (!string.IsNullOrWhiteSpace(argumentConnection))
        {
            return argumentConnection;
        }

        var nestedEnvConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        if (!string.IsNullOrWhiteSpace(nestedEnvConnection))
        {
            return nestedEnvConnection;
        }

        var flatEnvConnection = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(flatEnvConnection))
        {
            return flatEnvConnection;
        }

        return "Host=localhost;Port=5433;Database=soweit_optimizer;Username=dlstannhausen;Password=dlstannhausen";
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
