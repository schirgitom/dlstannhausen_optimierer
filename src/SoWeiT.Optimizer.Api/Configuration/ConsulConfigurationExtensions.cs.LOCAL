using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Winton.Extensions.Configuration.Consul;

namespace SoWeiT.Optimizer.Api.Configuration;

internal static class ConsulConfigurationExtensions
{
    private const string ConsulUrlEnvironmentVariable = "CONSUL_URL";
    private const string ConsulUrlSectionKey = "Consul:Url";
    private const string RootKeyPrefix = "DLTannhausen";

    public static ConsulLoadResult AddConsulConfiguration(this ConfigurationManager configuration, IHostEnvironment environment)
    {
        var consulUrl = ResolveConsulUrl(configuration);
        if (string.IsNullOrWhiteSpace(consulUrl))
        {
            return ConsulLoadResult.Disabled(ConsulUrlEnvironmentVariable);
        }

        if (!Uri.TryCreate(consulUrl, UriKind.Absolute, out var address))
        {
            throw new InvalidOperationException(
                $"Consul URL is invalid. Set '{ConsulUrlEnvironmentVariable}' or '{ConsulUrlSectionKey}' with an absolute URL.");
        }

        var keys = GetKeys(environment.ApplicationName);
        var result = new ConsulLoadResult(ConsulUrlEnvironmentVariable, address, keys);

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

    private static string? ResolveConsulUrl(IConfiguration configuration)
    {
        var envValue = Environment.GetEnvironmentVariable(ConsulUrlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        var standardEnvValue = Environment.GetEnvironmentVariable("Consul__Url");
        if (!string.IsNullOrWhiteSpace(standardEnvValue))
        {
            return standardEnvValue;
        }

        return configuration[ConsulUrlSectionKey];
    }

    private static string[] GetKeys(string applicationName)
    {
        var prefix = $"{RootKeyPrefix}/{applicationName}";
        return
        [
            $"{prefix}/serilog.json",
            $"{prefix}/connectionStrings.json",
            $"{prefix}/rabbitMq.json",
            $"{prefix}/optimizerStateStore.json",
            $"{prefix}/optimizerSessionRecovery.json",
            $"{prefix}/app.json"
        ];
    }

    internal sealed class ConsulLoadResult(
        string environmentVariableName,
        Uri address,
        IReadOnlyList<string> keys)
    {
        private readonly List<ConsulLoadFailure> _failures = [];

        public string EnvironmentVariableName { get; } = environmentVariableName;

        public Uri Address { get; } = address;

        public IReadOnlyList<string> Keys { get; } = keys;

        public IReadOnlyList<ConsulLoadFailure> Failures => _failures;

        public bool HasFailures => _failures.Count > 0;

        public bool Enabled { get; } = true;

        private ConsulLoadResult(string environmentVariableName)
            : this(environmentVariableName, new Uri("http://localhost"), [])
        {
            Enabled = false;
        }

        public static ConsulLoadResult Disabled(string environmentVariableName)
        {
            return new ConsulLoadResult(environmentVariableName);
        }

        public void RecordFailure(string key, Exception exception)
        {
            if (_failures.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _failures.Add(new ConsulLoadFailure(key, exception));
        }
    }

    internal sealed record ConsulLoadFailure(string Key, Exception Exception);
}
