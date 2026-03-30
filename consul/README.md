## Consul layout

The API supports layered configuration with this precedence (highest to lowest):

1. Environment variables
2. Consul KV
3. `appsettings*.json`

Consul is optional for the API. To enable Consul loading, set `CONSUL_URL` (or `Consul:Url` in appsettings).

The Persister Worker still reads runtime configuration from Consul and expects `CONSUL_URL`.

Key prefix pattern:

`DLTannhausen/<ApplicationName>/...`

Applications:

- `SoWeiT.Optimizer.Api`
- `SoWeiT.Optimizer.Persister.Worker`

### API keys

- `DLTannhausen/SoWeiT.Optimizer.Api/serilog.json`
- `DLTannhausen/SoWeiT.Optimizer.Api/connectionStrings.json`
- `DLTannhausen/SoWeiT.Optimizer.Api/rabbitMq.json`
- `DLTannhausen/SoWeiT.Optimizer.Api/optimizerStateStore.json`
- `DLTannhausen/SoWeiT.Optimizer.Api/optimizerSessionRecovery.json`
- `DLTannhausen/SoWeiT.Optimizer.Api/app.json`

### Worker keys

- `DLTannhausen/SoWeiT.Optimizer.Persister.Worker/serilog.json`
- `DLTannhausen/SoWeiT.Optimizer.Persister.Worker/connectionStrings.json`
- `DLTannhausen/SoWeiT.Optimizer.Persister.Worker/rabbitMq.json`

## Import examples

With Consul CLI:

```powershell
consul kv put DLTannhausen/SoWeiT.Optimizer.Api/serilog.json @consul/DLTannhausen/SoWeiT.Optimizer.Api/Development/serilog.json
consul kv put DLTannhausen/SoWeiT.Optimizer.Api/connectionStrings.json @consul/DLTannhausen/SoWeiT.Optimizer.Api/Development/connectionStrings.json
consul kv put DLTannhausen/SoWeiT.Optimizer.Api/rabbitMq.json @consul/DLTannhausen/SoWeiT.Optimizer.Api/Development/rabbitMq.json
consul kv put DLTannhausen/SoWeiT.Optimizer.Api/optimizerStateStore.json @consul/DLTannhausen/SoWeiT.Optimizer.Api/Development/optimizerStateStore.json
consul kv put DLTannhausen/SoWeiT.Optimizer.Api/optimizerSessionRecovery.json @consul/DLTannhausen/SoWeiT.Optimizer.Api/Development/optimizerSessionRecovery.json
consul kv put DLTannhausen/SoWeiT.Optimizer.Api/app.json @consul/DLTannhausen/SoWeiT.Optimizer.Api/Development/app.json

consul kv put DLTannhausen/SoWeiT.Optimizer.Persister.Worker/serilog.json @consul/DLTannhausen/SoWeiT.Optimizer.Persister.Worker/Development/serilog.json
consul kv put DLTannhausen/SoWeiT.Optimizer.Persister.Worker/connectionStrings.json @consul/DLTannhausen/SoWeiT.Optimizer.Persister.Worker/Development/connectionStrings.json
consul kv put DLTannhausen/SoWeiT.Optimizer.Persister.Worker/rabbitMq.json @consul/DLTannhausen/SoWeiT.Optimizer.Persister.Worker/Development/rabbitMq.json
```
