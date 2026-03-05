namespace SoWeiT.Optimizer.Models;

/// <summary>
/// Ergebnis einer Optimierungsausfuehrung.
/// </summary>
public readonly record struct OptimizationResult(double[] Schaltzustand, double[] ResOpt);
