namespace SoWeiT.Optimizer.Models;

/// <summary>
/// Datenstruktur analog zur Pyomo-Datenaufbereitung.
/// </summary>
public sealed class SolverInputData
{
    public required string[] NutzerLabels { get; init; }

    public required Dictionary<string, double> VerbrauchJeNutzer { get; init; }
}
