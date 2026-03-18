using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Winton.Extensions.Configuration.Consul;

namespace SoWeiT.Optimizer.Persister.Worker.Configuration;

internal static class ConsulConfigurationExtensions
{
    private const string ConsulUrlEnvironmentVariable = "CONSUL_URL";
    private const string ConsulEnabledKey = "Consul:Enabled";
    private const string ConsulUrlKey = "Consul:Url";
    private const string ConsulRootKeyPrefixKey = "Consul:RootKeyPrefix";
    private const string RootKeyPrefix = "DLTannhausen";

    public static ConsulLoadResult AddConsulConfiguration(this ConfigurationManager configuration, IHostEnvironment environment)
    {
        var consulUrl = ResolveConsulUrl(configuration);
        var isEnabled = ResolveConsulEnabled(configuration, consulUrl);
        if (!isEnabled)
        {
            return ConsulLoadResult.Disabled(ConsulEnabledKey);
        }

        if (string.IsNullOrWhiteSpace(consulUrl))
        {
            throw new InvalidOperationException($"""
                Consul is enabled, but no URL is configured.
                Set '{ConsulUrlKey}' (or env var 'Consul__Url' / '{ConsulUrlEnvironmentVariable}'),
                or disable Consul with '{ConsulEnabledKey}' / 'Consul__Enabled=false'.
                """);
        }

        if (!Uri.TryCreate(consulUrl, UriKind.Absolute, out var address))
        {
            throw new InvalidOperationException(
                $"Configured Consul URL ('{consulUrl}') must be an absolute URL.");
        }

        var rootKeyPrefix = configuration[ConsulRootKeyPrefixKey];
        var keys = GetKeys(environment.ApplicationName, rootKeyPrefix);
        var result = new ConsulLoadResult(ConsulUrlEnvironmentVariable, address, keys, isEnabled: true);

        foreach (var key in keys)
        {
            configuration.AddConsul(
                key,
                options =>
                {
                    options.Optional = true;
                    options.ReloadOnChange = true;
                    options.ConsulConfigurationOptions = consul => { consul.Address = address; };
                    options.OnLoadException = context =>
                    {
                        result.RecordFailure(key, context.Exception);
                        context.Ignore = true;
                    };
                });
        }

        return result;
    }

    private static bool ResolveConsulEnabled(IConfiguration configuration, string? consulUrl)
    {
        var configured = configuration.GetValue<bool?>(ConsulEnabledKey);
        if (configured.HasValue)
        {
            return configured.Value;
        }

        return !string.IsNullOrWhiteSpace(consulUrl);
    }

    private static string? ResolveConsulUrl(IConfiguration configuration)
    {
        var configuredUrl = configuration[ConsulUrlKey];
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl;
        }

        return Environment.GetEnvironmentVariable(ConsulUrlEnvironmentVariable);
    }

    private static string[] GetKeys(string applicationName, string? configuredRootKeyPrefix)
    {
        var rootKeyPrefix = string.IsNullOrWhiteSpace(configuredRootKeyPrefix)
            ? RootKeyPrefix
            : configuredRootKeyPrefix;
        var prefix = $"{rootKeyPrefix}/{applicationName}";
        return
        [
            $"{prefix}/serilog.json",
            $"{prefix}/connectionStrings.json",
            $"{prefix}/rabbitMq.json"
        ];
    }

    internal sealed class ConsulLoadResult(
        string environmentVariableName,
        Uri address,
        IReadOnlyList<string> keys,
        bool isEnabled)
    {
        private readonly List<ConsulLoadFailure> _failures = [];

        public string EnvironmentVariableName { get; } = environmentVariableName;

        public Uri Address { get; } = address;

        public IReadOnlyList<string> Keys { get; } = keys;

        public IReadOnlyList<ConsulLoadFailure> Failures => _failures;

        public bool HasFailures => _failures.Count > 0;

        public bool IsEnabled { get; } = isEnabled;

        public void RecordFailure(string key, Exception exception)
        {
            if (_failures.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _failures.Add(new ConsulLoadFailure(key, exception));
        }

        public static ConsulLoadResult Disabled(string environmentVariableName)
        {
            return new ConsulLoadResult(
                environmentVariableName,
                new Uri("http://localhost"),
                Array.Empty<string>(),
                isEnabled: false);
        }
    }

    internal sealed record ConsulLoadFailure(string Key, Exception Exception);
}
