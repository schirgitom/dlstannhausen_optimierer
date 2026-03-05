using Microsoft.Extensions.Logging;

namespace SoWeiT.Optimizer.Solver;

public sealed class GreedyFallbackSolver : IOptimizerSolver
{
    private const double ObjectiveFairnessMultiplier = 100.0;

    public bool TrySolve(
        double pvErzeugungWatt,
        ReadOnlySpan<double> verbrauch,
        ReadOnlySpan<double> faktor,
        Span<double> result,
        ILogger logger)
    {
        logger.LogInformation(
            "GreedyFallbackSolver start. PvErzeugungWatt={PvErzeugungWatt}, NutzerAnzahl={NutzerAnzahl}",
            pvErzeugungWatt,
            verbrauch.Length);
        var n = verbrauch.Length;
        var order = new int[n];
        var scores = new double[n];
        for (var i = 0; i < n; i++)
        {
            order[i] = i;
            scores[i] = verbrauch[i] * (1.0 + ObjectiveFairnessMultiplier * faktor[i]);
        }

        Array.Sort(order, (a, b) =>
        {
            var scoreCmp = scores[b].CompareTo(scores[a]);
            return scoreCmp != 0 ? scoreCmp : a.CompareTo(b);
        });

        var pvRest = pvErzeugungWatt;

        for (var k = 0; k < order.Length; k++)
        {
            var i = order[k];
            var demand = verbrauch[i];
            if (demand <= 0.0)
            {
                result[i] = 0.0;
                logger.LogDebug("GreedyFallbackSolver skip non-positive demand: Nutzer={Nutzer}", i);
                continue;
            }

            if (pvRest >= demand)
            {
                result[i] = demand;
                pvRest -= demand;
                logger.LogDebug("GreedyFallbackSolver accepted: Nutzer={Nutzer}, Demand={Demand}, PvRest={PvRest}", i, demand, pvRest);
                continue;
            }

            result[i] = 0.0;
            logger.LogDebug("GreedyFallbackSolver rejected: Nutzer={Nutzer}, Demand={Demand}, PvRest={PvRest}", i, demand, pvRest);
        }

        logger.LogInformation("GreedyFallbackSolver done.");
        return true;
    }
}
