using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SoWeiT.Optimizer.Models;
using SoWeiT.Optimizer.Solver;

namespace SoWeiT.Optimizer;

/// <summary>
/// Stateful Portierung des Python-Direktleitungsoptimierers.
/// Eine Instanz repraesentiert genau ein laufendes DLS-System ueber die Zeit.
/// </summary>
public sealed class Optimierer
{
    /// <summary>
    /// Schaltkontingent-Zuwachs pro Optimierer-Aufruf (entspricht Python: +0.032).
    /// </summary>
    public const double SwitchBudgetIncrementPerRun = 0.032;

    /// <summary>
    /// Gewichtung des Fairness-Terms in der Zielfunktion (entspricht Python: *100).
    /// </summary>
    public const double ObjectiveFairnessMultiplier = 100.0;

    /// <summary>
    /// Startwert fuer den Faktor in der Fairnesssortierung (entspricht Python: 0.7).
    /// </summary>
    public const double FairnessStartValue = 0.7;

    /// <summary>
    /// Inkrement fuer den Faktor in der Fairnesssortierung (entspricht Python: +0.1).
    /// </summary>
    public const double FairnessStep = 0.1;

    private readonly ILogger<Optimierer> _logger;
    private readonly IReadOnlyList<IOptimizerSolver> _solvers;

    private double _erzeugung;
    private readonly double[] _faktor;
    private readonly double[,] _verteilung;
    private readonly double[] _schaltkontingent;
    private readonly DateTimeOffset[] _schaltzeit;
    private readonly double[,] _schaltzustand;

    private double[]? _pvVerbrauchEnergieStand;
    private double[]? _verbrauchEnergieStand;

    /// <summary>
    /// Anzahl der Nutzer.
    /// </summary>
    public int N { get; }

    /// <summary>
    /// Sperrzeit in Sekunden, waehrend der nur bei positivem Schaltkontingent geschalten werden darf.
    /// </summary>
    public int Sperrzeit1 { get; }

    /// <summary>
    /// Sperrzeit in Sekunden, waehrend der auch bei positivem Schaltkontingent nicht geschalten werden darf.
    /// </summary>
    public int Sperrzeit2 { get; }

    /// <summary>
    /// Aktuelle PV-Erzeugung aus dem letzten Run-Aufruf.
    /// </summary>
    public double Erzeugung => _erzeugung;

    /// <summary>
    /// Fairness-Faktoren je Nutzer.
    /// </summary>
    public double[] Faktor => _faktor;

    /// <summary>
    /// Kumulierte Verteilung [Nutzer, 0=pv_zugewiesen; 1=gesamtverbrauch].
    /// </summary>
    public double[,] Verteilung => _verteilung;

    /// <summary>
    /// Schaltkontingent je Nutzer.
    /// </summary>
    public double[] Schaltkontingent => _schaltkontingent;

    /// <summary>
    /// Letzter Ausschaltzeitpunkt je Nutzer.
    /// </summary>
    public DateTimeOffset[] Schaltzeit => _schaltzeit;

    /// <summary>
    /// Schaltzustaende [Nutzer, 0=vorher; 1=aktuell].
    /// </summary>
    public double[,] Schaltzustand => _schaltzustand;

    /// <summary>
    /// Letzter Zaehlerstand der ueber DLS zugewiesenen Energie.
    /// </summary>
    public double[]? PvVerbrauchEnergieStand => _pvVerbrauchEnergieStand;

    /// <summary>
    /// Letzter Gesamtverbrauchs-Zaehlerstand.
    /// </summary>
    public double[]? VerbrauchEnergieStand => _verbrauchEnergieStand;

    /// <summary>
    /// Initialisiert eine neue Instanz des Optimierers.
    /// Zeitstempel werden als DateTimeOffset in konsistenter Zeitzone (empfohlen UTC) verarbeitet.
    /// </summary>
    public Optimierer(
        int n,
        int sperrzeit1,
        int sperrzeit2,
        ILogger<Optimierer>? logger = null,
        IReadOnlyList<IOptimizerSolver>? solvers = null)
    {
        if (n <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "N must be > 0.");
        }

        N = n;
        Sperrzeit1 = sperrzeit1;
        Sperrzeit2 = sperrzeit2;
        _logger = logger ?? NullLogger<Optimierer>.Instance;

        _faktor = new double[n];
        _verteilung = new double[n, 2];
        _schaltkontingent = new double[n];
        _schaltzeit = Enumerable.Repeat(DateTimeOffset.UnixEpoch, n).ToArray();
        _schaltzustand = new double[n, 2];

        _solvers = solvers ?? new IOptimizerSolver[]
        {
            new OrtToolsSolver(),
            new GreedyFallbackSolver()
        };

        _logger.LogInformation(
            "Optimierer created: N={N}, Sperrzeit1={Sperrzeit1}, Sperrzeit2={Sperrzeit2}, Solvers={SolverCount}",
            N,
            Sperrzeit1,
            Sperrzeit2,
            _solvers.Count);
    }

    /// <summary>
    /// Python-aequivalente Datenaufbereitung (Nutzerlabels und Verbrauchstabelle).
    /// </summary>
    public SolverInputData PrepareData(ReadOnlySpan<double> verbrauch)
    {
        _logger.LogInformation("PrepareData start. Verbrauch={Verbrauch}", FormatSpan(verbrauch));
        ValidateLength(verbrauch.Length, nameof(verbrauch));

        var labels = new string[N];
        var map = new Dictionary<string, double>(N, StringComparer.Ordinal);
        for (var i = 0; i < N; i++)
        {
            var label = $"N{i + 1}";
            labels[i] = label;
            map[label] = verbrauch[i];
        }

        var data = new SolverInputData
        {
            NutzerLabels = labels,
            VerbrauchJeNutzer = map
        };

        _logger.LogInformation("PrepareData done. Labels={Labels}", string.Join(",", data.NutzerLabels));
        return data;
    }

    /// <summary>
    /// Pre-processing zur Schaltfreigabepruefung gemaess Sperrzeiten und Schaltkontingent.
    /// </summary>
    public double[] Preprocessing(DateTimeOffset zeitstempel, ReadOnlySpan<double> verbrauch)
    {
        _logger.LogInformation("Preprocessing start. Zeitstempel={Zeitstempel}, Verbrauch={Verbrauch}", zeitstempel, FormatSpan(verbrauch));
        ValidateLength(verbrauch.Length, nameof(verbrauch));

        var schaltfreigabe = new double[N];

        for (var i = 0; i < N; i++)
        {
            _schaltkontingent[i] += SwitchBudgetIncrementPerRun;
            _schaltzustand[i, 0] = _schaltzustand[i, 1];
            _logger.LogDebug(
                "Preprocessing budget carryover: Nutzer={Nutzer}, Kontingent={Kontingent}, SchaltzustandVorher={SchaltzustandVorher}",
                i,
                _schaltkontingent[i],
                _schaltzustand[i, 0]);
        }

        var result = new double[N];
        for (var j = 0; j < N; j++)
        {
            if (_schaltzustand[j, 0] == 1.0)
            {
                schaltfreigabe[j] = 1.0;
            }
            else
            {
                var secondsSinceLastSwitch = (zeitstempel - _schaltzeit[j]).TotalSeconds;
                if (secondsSinceLastSwitch >= Sperrzeit1)
                {
                    schaltfreigabe[j] = 1.0;
                }
                else if (_schaltkontingent[j] >= 1.0 && secondsSinceLastSwitch >= Sperrzeit2)
                {
                    schaltfreigabe[j] = 1.0;
                }
                else
                {
                    schaltfreigabe[j] = 0.0;
                }
            }

            result[j] = verbrauch[j] * schaltfreigabe[j];
            _logger.LogDebug(
                "Preprocessing user evaluated: Nutzer={Nutzer}, Schaltfreigabe={Schaltfreigabe}, ErgebnisVerbrauch={ErgebnisVerbrauch}",
                j,
                schaltfreigabe[j],
                result[j]);
        }

        _logger.LogInformation("Preprocessing done. Ergebnis={Ergebnis}", FormatArray(result));
        return result;
    }

    /// <summary>
    /// Post-processing zur Aktualisierung von Schaltkontingent und Schaltzeitpunkten.
    /// </summary>
    public void Postprocessing(DateTimeOffset zeitstempel)
    {
        _logger.LogInformation("Postprocessing start. Zeitstempel={Zeitstempel}", zeitstempel);
        for (var j = 0; j < N; j++)
        {
            if (_schaltzustand[j, 1] == 1.0 && _schaltzustand[j, 0] == 0.0)
            {
                _schaltkontingent[j] -= 1.0;
                _logger.LogDebug("Postprocessing switch on: Nutzer={Nutzer}, NeuesKontingent={Kontingent}", j, _schaltkontingent[j]);
            }
            else if (_schaltzustand[j, 1] == 0.0 && _schaltzustand[j, 0] == 1.0)
            {
                _schaltzeit[j] = zeitstempel;
                _schaltkontingent[j] -= 1.0;
                _logger.LogDebug(
                    "Postprocessing switch off: Nutzer={Nutzer}, NeuesKontingent={Kontingent}, NeueSchaltzeit={Schaltzeit}",
                    j,
                    _schaltkontingent[j],
                    _schaltzeit[j]);
            }
        }

        _logger.LogInformation("Postprocessing done. Kontingente={Kontingente}", FormatArray(_schaltkontingent));
    }

    /// <summary>
    /// Fuehrt die Optimierung fuer einen Zeitschritt aus.
    /// </summary>
    public OptimizationResult Run(double erzeugungIn, ReadOnlySpan<double> verbrauch, DateTimeOffset zeitstempel)
    {
        _logger.LogInformation(
            "Run start. ErzeugungIn={ErzeugungIn}, Zeitstempel={Zeitstempel}, Verbrauch={Verbrauch}",
            erzeugungIn,
            zeitstempel,
            FormatSpan(verbrauch));
        ValidateLength(verbrauch.Length, nameof(verbrauch));

        _erzeugung = erzeugungIn;

        var preprocessedVerbrauch = Preprocessing(zeitstempel, verbrauch);
        var resOpt = new double[N];

        var solved = false;
        foreach (var solver in _solvers)
        {
            _logger.LogInformation("Trying solver {Solver}", solver.GetType().Name);
            if (!solver.TrySolve(_erzeugung, preprocessedVerbrauch, _faktor, resOpt, _logger))
            {
                _logger.LogWarning("Solver {Solver} failed to solve.", solver.GetType().Name);
                continue;
            }

            solved = true;
            _logger.LogInformation("Solver {Solver} succeeded. Result={Result}", solver.GetType().Name, FormatArray(resOpt));
            break;
        }

        if (!solved)
        {
            _logger.LogError("No solver could produce a feasible result. Returning conservative all-off state.");
            Array.Clear(resOpt);
        }

        for (var j = 0; j < N; j++)
        {
            _schaltzustand[j, 1] = resOpt[j] > 0.0 ? 1.0 : 0.0;
            _logger.LogDebug("Run schaltzustand calculated: Nutzer={Nutzer}, ZustandAktuell={ZustandAktuell}", j, _schaltzustand[j, 1]);
        }

        Postprocessing(zeitstempel);

        var currentSwitchState = new double[N];
        for (var i = 0; i < N; i++)
        {
            currentSwitchState[i] = _schaltzustand[i, 1];
        }

        var finalResult = new OptimizationResult(currentSwitchState, resOpt);
        _logger.LogInformation(
            "Run done. Schaltzustand={Schaltzustand}, ResOpt={ResOpt}",
            FormatArray(finalResult.Schaltzustand),
            FormatArray(finalResult.ResOpt));
        return finalResult;
    }

    /// <summary>
    /// Aktualisiert Verteilungsdaten auf Basis absoluter Zaehlerstaende.
    /// </summary>
    public void UpdateVerteilungMittelsEnergie(
        ReadOnlySpan<double> pvVerbrauchEnergieStand,
        ReadOnlySpan<double> verbrauchEnergieStand)
    {
        _logger.LogInformation(
            "UpdateVerteilungMittelsEnergie start. PvVerbrauchEnergieStand={Pv}, VerbrauchEnergieStand={Verbrauch}",
            FormatSpan(pvVerbrauchEnergieStand),
            FormatSpan(verbrauchEnergieStand));
        ValidateLength(pvVerbrauchEnergieStand.Length, nameof(pvVerbrauchEnergieStand));
        ValidateLength(verbrauchEnergieStand.Length, nameof(verbrauchEnergieStand));

        if (_pvVerbrauchEnergieStand is not null && _verbrauchEnergieStand is not null)
        {
            var pvVerbrauchDeltas = new double[N];
            var verbrauchDeltas = new double[N];

            for (var i = 0; i < N; i++)
            {
                pvVerbrauchDeltas[i] = pvVerbrauchEnergieStand[i] - _pvVerbrauchEnergieStand[i];
                verbrauchDeltas[i] = verbrauchEnergieStand[i] - _verbrauchEnergieStand[i];
                _logger.LogDebug(
                    "Counter delta: Nutzer={Nutzer}, PvDelta={PvDelta}, VerbrauchDelta={VerbrauchDelta}",
                    i,
                    pvVerbrauchDeltas[i],
                    verbrauchDeltas[i]);
            }

            UpdateVerteilung(pvVerbrauchDeltas, verbrauchDeltas);
        }

        _pvVerbrauchEnergieStand = pvVerbrauchEnergieStand.ToArray();
        _verbrauchEnergieStand = verbrauchEnergieStand.ToArray();
        _logger.LogInformation("UpdateVerteilungMittelsEnergie done.");
    }

    /// <summary>
    /// Aktualisiert Verteilungsdaten auf Basis von Deltawerten.
    /// </summary>
    public void UpdateVerteilung(ReadOnlySpan<double> pvVerbrauchDeltas, ReadOnlySpan<double> verbrauchDeltas)
    {
        _logger.LogInformation(
            "UpdateVerteilung start. PvVerbrauchDeltas={PvDeltas}, VerbrauchDeltas={VerbrauchDeltas}",
            FormatSpan(pvVerbrauchDeltas),
            FormatSpan(verbrauchDeltas));
        ValidateLength(pvVerbrauchDeltas.Length, nameof(pvVerbrauchDeltas));
        ValidateLength(verbrauchDeltas.Length, nameof(verbrauchDeltas));

        for (var i = 0; i < N; i++)
        {
            _verteilung[i, 0] += pvVerbrauchDeltas[i];
            _verteilung[i, 1] += verbrauchDeltas[i];
            _logger.LogDebug(
                "Verteilung updated: Nutzer={Nutzer}, PvZugewiesen={PvZugewiesen}, Gesamtverbrauch={Gesamtverbrauch}",
                i,
                _verteilung[i, 0],
                _verteilung[i, 1]);
        }

        UpdateFaktor();
        _logger.LogInformation("UpdateVerteilung done.");
    }

    private void UpdateFaktor()
    {
        _logger.LogInformation("UpdateFaktor start.");
        var pVerteilung = new double[N];

        for (var i = 0; i < N; i++)
        {
            pVerteilung[i] = _verteilung[i, 0] / _verteilung[i, 1];
            _logger.LogDebug("UpdateFaktor p_verteilung: Nutzer={Nutzer}, Wert={Wert}", i, pVerteilung[i]);
        }

        var faktorwert = FairnessStartValue;
        for (var i = 0; i < N; i++)
        {
            var index = ArgMaxLikeNumpy(pVerteilung);
            _faktor[index] = faktorwert;
            pVerteilung[index] = -1.0;
            _logger.LogDebug("UpdateFaktor ranking step: Rang={Rang}, Nutzer={Nutzer}, Faktor={Faktor}", i, index, _faktor[index]);
            faktorwert += FairnessStep;
        }

        _logger.LogInformation("UpdateFaktor done. Faktor={Faktor}", FormatArray(_faktor));
    }

    private static int ArgMaxLikeNumpy(ReadOnlySpan<double> values)
    {
        var bestIndex = 0;
        var bestValue = values[0];

        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > bestValue)
            {
                bestValue = values[i];
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void ValidateLength(int length, string paramName)
    {
        if (length != N)
        {
            _logger.LogError("ValidateLength failed for {ParamName}. Expected={Expected}, Got={Got}", paramName, N, length);
            throw new ArgumentException($"Expected length {N}, got {length}.", paramName);
        }
    }

    /// <summary>
    /// Erstellt einen serialisierbaren Snapshot des kompletten internen Zustands.
    /// </summary>
    public OptimiererStateSnapshot CreateSnapshot()
    {
        _logger.LogInformation("CreateSnapshot start.");
        var snapshot = new OptimiererStateSnapshot
        {
            N = N,
            Sperrzeit1 = Sperrzeit1,
            Sperrzeit2 = Sperrzeit2,
            Erzeugung = _erzeugung,
            Faktor = _faktor.ToArray(),
            Verteilung = ToJagged(_verteilung),
            Schaltkontingent = _schaltkontingent.ToArray(),
            Schaltzeit = _schaltzeit.ToArray(),
            Schaltzustand = ToJagged(_schaltzustand),
            PvVerbrauchEnergieStand = _pvVerbrauchEnergieStand?.ToArray(),
            VerbrauchEnergieStand = _verbrauchEnergieStand?.ToArray()
        };

        _logger.LogInformation("CreateSnapshot done.");
        return snapshot;
    }

    /// <summary>
    /// Stellt den kompletten internen Zustand aus einem Snapshot wieder her.
    /// </summary>
    public void ApplySnapshot(OptimiererStateSnapshot snapshot)
    {
        _logger.LogInformation("ApplySnapshot start.");
        if (snapshot.N != N || snapshot.Sperrzeit1 != Sperrzeit1 || snapshot.Sperrzeit2 != Sperrzeit2)
        {
            throw new InvalidOperationException("Snapshot metadata does not match optimizer configuration.");
        }

        ValidateLength(snapshot.Faktor.Length, nameof(snapshot.Faktor));
        ValidateLength(snapshot.Schaltkontingent.Length, nameof(snapshot.Schaltkontingent));
        ValidateLength(snapshot.Schaltzeit.Length, nameof(snapshot.Schaltzeit));
        ValidateLength(snapshot.Schaltzustand.Length, nameof(snapshot.Schaltzustand));
        ValidateLength(snapshot.Verteilung.Length, nameof(snapshot.Verteilung));

        _erzeugung = snapshot.Erzeugung;
        Array.Copy(snapshot.Faktor, _faktor, N);
        CopyFromJagged(snapshot.Verteilung, _verteilung, expectedColumns: 2);
        Array.Copy(snapshot.Schaltkontingent, _schaltkontingent, N);
        Array.Copy(snapshot.Schaltzeit, _schaltzeit, N);
        CopyFromJagged(snapshot.Schaltzustand, _schaltzustand, expectedColumns: 2);
        _pvVerbrauchEnergieStand = snapshot.PvVerbrauchEnergieStand?.ToArray();
        _verbrauchEnergieStand = snapshot.VerbrauchEnergieStand?.ToArray();
        _logger.LogInformation("ApplySnapshot done.");
    }

    private static string FormatSpan(ReadOnlySpan<double> values)
    {
        return FormatArray(values.ToArray());
    }

    private static string FormatArray(double[] values)
    {
        return "[" + string.Join(", ", values.Select(v => v.ToString("G17"))) + "]";
    }

    private static double[][] ToJagged(double[,] matrix)
    {
        var rows = matrix.GetLength(0);
        var cols = matrix.GetLength(1);
        var result = new double[rows][];

        for (var r = 0; r < rows; r++)
        {
            result[r] = new double[cols];
            for (var c = 0; c < cols; c++)
            {
                result[r][c] = matrix[r, c];
            }
        }

        return result;
    }

    private static void CopyFromJagged(double[][] source, double[,] target, int expectedColumns)
    {
        var rows = target.GetLength(0);
        var cols = target.GetLength(1);
        if (cols != expectedColumns)
        {
            throw new InvalidOperationException("Unexpected target columns.");
        }

        if (source.Length != rows)
        {
            throw new InvalidOperationException("Unexpected source row count.");
        }

        for (var r = 0; r < rows; r++)
        {
            if (source[r].Length != cols)
            {
                throw new InvalidOperationException("Unexpected source column count.");
            }

            for (var c = 0; c < cols; c++)
            {
                target[r, c] = source[r][c];
            }
        }
    }
}
