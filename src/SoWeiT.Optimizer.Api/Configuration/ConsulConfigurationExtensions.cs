using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Winton.Extensions.Configuration.Consul;

namespace SoWeiT.Optimizer.Api.Configuration;

internal static class ConsulConfigurationExtensions
{
    public static void AddConsulConfiguration(this ConfigurationManager configuration, IHostEnvironment environment)
    {
        var bootstrap = configuration.GetSection("Consul").Get<ConsulBootstrapOptions>() ?? new ConsulBootstrapOptions();
        var address = new Uri(bootstrap.Address, UriKind.Absolute);
        var key = ResolveKey(bootstrap, environment);

        configuration.AddConsul(
            key,
            options =>
            {
                options.ConsulConfigurationOptions = consul => { consul.Address = address; };
                options.Optional = bootstrap.Optional;
                options.ReloadOnChange = bootstrap.ReloadOnChange;
                options.OnLoadException = context => { context.Ignore = bootstrap.Optional; };
            });
    }

    private static string ResolveKey(ConsulBootstrapOptions options, IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.Key))
        {
            return options.Key.Trim('/');
        }

        var prefix = string.IsNullOrWhiteSpace(options.KeyPrefix)
            ? "DLTannhausen"
            : options.KeyPrefix.Trim('/');

        return $"{prefix}/{environment.ApplicationName}/{environment.EnvironmentName}";
    }

    private sealed class ConsulBootstrapOptions
    {
        public string Address { get; init; } = "http://localhost:8500";

        public string? Key { get; init; }

        public string KeyPrefix { get; init; } = "DLTannhausen";

        public bool Optional { get; init; }

        public bool ReloadOnChange { get; init; } = true;
    }
}
