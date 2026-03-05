namespace SoWeiT.Optimizer.Models;

/// <summary>
/// Vollstaendiger serialisierbarer Zustands-Snapshot des Optimierers.
/// </summary>
public sealed class OptimiererStateSnapshot
{
    public required int N { get; init; }

    public required int Sperrzeit1 { get; init; }

    public required int Sperrzeit2 { get; init; }

    public required double Erzeugung { get; init; }

    public required double[] Faktor { get; init; }

    public required double[][] Verteilung { get; init; }

    public required double[] Schaltkontingent { get; init; }

    public required DateTimeOffset[] Schaltzeit { get; init; }

    public required double[][] Schaltzustand { get; init; }

    public double[]? PvVerbrauchEnergieStand { get; init; }

    public double[]? VerbrauchEnergieStand { get; init; }
}
