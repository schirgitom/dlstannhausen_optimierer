using SoWeiT.Optimizer.Models;

namespace SoWeiT.Optimizer.Persistence.Redis.Models;

public sealed class PersistedOptimizerSession
{
    public required Guid SessionId { get; init; }

    public required bool UseOrTools { get; init; }

    public required bool UseGreedyFallback { get; init; }

    public required OptimiererStateSnapshot Snapshot { get; init; }
}

