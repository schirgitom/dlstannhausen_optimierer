using SoWeiT.Optimizer.Models;

namespace SoWeiT.Optimizer.Api.Models;

public sealed class PersistedOptimizerSession
{
    public required Guid SessionId { get; init; }

    public required bool UseOrTools { get; init; }

    public required bool UseGreedyFallback { get; init; }

    public required OptimiererStateSnapshot Snapshot { get; init; }
}
