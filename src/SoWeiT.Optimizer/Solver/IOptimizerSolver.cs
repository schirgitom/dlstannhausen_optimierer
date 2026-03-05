using Microsoft.Extensions.Logging;

namespace SoWeiT.Optimizer.Solver;

public interface IOptimizerSolver
{
    bool TrySolve(
        double pvErzeugungWatt,
        ReadOnlySpan<double> verbrauch,
        ReadOnlySpan<double> faktor,
        Span<double> result,
        ILogger logger);
}
