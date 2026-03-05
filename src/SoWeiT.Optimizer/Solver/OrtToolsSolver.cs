using Google.OrTools.LinearSolver;
using Microsoft.Extensions.Logging;
using LinearSolver = Google.OrTools.LinearSolver.Solver;

namespace SoWeiT.Optimizer.Solver;

public sealed class OrtToolsSolver : IOptimizerSolver
{
    private const string PrimarySolverId = "CBC_MIXED_INTEGER_PROGRAMMING";
    private const string SecondarySolverId = "SCIP_MIXED_INTEGER_PROGRAMMING";
    private const double ObjectiveFairnessMultiplier = 100.0;

    public bool TrySolve(
        double pvErzeugungWatt,
        ReadOnlySpan<double> verbrauch,
        ReadOnlySpan<double> faktor,
        Span<double> result,
        ILogger logger)
    {
        try
        {
            logger.LogInformation(
                "OrtToolsSolver start. PvErzeugungWatt={PvErzeugungWatt}, NutzerAnzahl={NutzerAnzahl}",
                pvErzeugungWatt,
                verbrauch.Length);
            var solver = CreateSolver();
            if (solver is null)
            {
                logger.LogWarning("OR-Tools solver backend not available.");
                return false;
            }

            var n = verbrauch.Length;
            var einAus = new Variable[n];

            for (var i = 0; i < n; i++)
            {
                einAus[i] = solver.MakeIntVar(0.0, 1.0, $"einaus_{i}");
            }

            var pvConstraint = solver.MakeConstraint(double.NegativeInfinity, pvErzeugungWatt, "pv_capacity");
            for (var i = 0; i < n; i++)
            {
                pvConstraint.SetCoefficient(einAus[i], verbrauch[i]);
            }

            var objective = solver.Objective();
            for (var i = 0; i < n; i++)
            {
                var coefficient = verbrauch[i] * (1.0 + ObjectiveFairnessMultiplier * faktor[i]);
                objective.SetCoefficient(einAus[i], coefficient);
            }

            objective.SetMaximization();

            var status = solver.Solve();
            if (status is not LinearSolver.ResultStatus.OPTIMAL and not LinearSolver.ResultStatus.FEASIBLE)
            {
                logger.LogWarning("OR-Tools returned non-feasible status: {Status}", status);
                return false;
            }

            for (var i = 0; i < n; i++)
            {
                result[i] = einAus[i].SolutionValue() > 0.5 ? verbrauch[i] : 0.0;
                logger.LogDebug("OrtToolsSolver decision: Nutzer={Nutzer}, Result={Result}", i, result[i]);
            }

            logger.LogInformation("OrtToolsSolver done successfully.");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OR-Tools solver execution failed.");
            return false;
        }
    }

    private static LinearSolver? CreateSolver()
    {
        return LinearSolver.CreateSolver(PrimarySolverId) ?? LinearSolver.CreateSolver(SecondarySolverId);
    }

}
