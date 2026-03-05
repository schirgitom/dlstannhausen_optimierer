namespace SoWeiT.Optimizer.Api.Contracts;

public sealed record CreateOptimizerSessionRequest(
    int N,
    int Sperrzeit1,
    int Sperrzeit2,
    bool UseOrTools = true,
    bool UseGreedyFallback = true);

public sealed record CreateOptimizerSessionResponse(Guid SessionId);

public sealed record RunRequest(double PvErzeugungWatt, double[] Verbrauch, DateTimeOffset Zeitstempel);

public sealed record PreprocessingRequest(double[] Verbrauch, DateTimeOffset Zeitstempel);

public sealed record PostprocessingRequest(DateTimeOffset Zeitstempel);

public sealed record UpdateVerteilungRequest(double[] PvVerbrauchDeltas, double[] VerbrauchDeltas);

public sealed record UpdateVerteilungMittelsEnergieRequest(double[] PvVerbrauchEnergieStand, double[] VerbrauchEnergieStand);

public sealed record PrepareDataRequest(double[] Verbrauch);

public sealed record RunResponse(double[] Schaltzustand, double[] ResOpt, double[] Messwerte);

public sealed record OptimizerStateResponse(
    int N,
    int Sperrzeit1,
    int Sperrzeit2,
    double Erzeugung,
    double[] Faktor,
    double[][] Verteilung,
    double[] Schaltkontingent,
    DateTimeOffset[] Schaltzeit,
    double[][] Schaltzustand,
    double[]? PvVerbrauchEnergieStand,
    double[]? VerbrauchEnergieStand);
