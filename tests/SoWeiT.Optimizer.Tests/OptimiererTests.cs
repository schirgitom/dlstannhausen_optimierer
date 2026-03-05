using SoWeiT.Optimizer.Solver;
using System.Globalization;

namespace SoWeiT.Optimizer.Tests;

public sealed class OptimiererTests
{
    [Fact]
    public void Preprocessing_BlocksUserDuringSperrzeit()
    {
        var now = new DateTimeOffset(2026, 2, 27, 10, 0, 0, TimeSpan.Zero);
        var sut = new Optimierer(2, sperrzeit1: 60, sperrzeit2: 120, solvers: new IOptimizerSolver[] { new OrtToolsSolver() });

        sut.Schaltzeit[0] = now;
        sut.Schaltzeit[1] = now.AddSeconds(-200);

        var result = sut.Preprocessing(now, new[] { 10.0, 20.0 });

        Assert.Equal(0.0, result[0]);
        Assert.Equal(20.0, result[1]);
        Assert.Equal(Optimierer.SwitchBudgetIncrementPerRun, sut.Schaltkontingent[0], 12);
        Assert.Equal(Optimierer.SwitchBudgetIncrementPerRun, sut.Schaltkontingent[1], 12);
    }

    [Fact]
    public void Postprocessing_UpdatesContingentAndSchaltzeit()
    {
        var now = new DateTimeOffset(2026, 2, 27, 10, 0, 0, TimeSpan.Zero);
        var sut = new Optimierer(2, sperrzeit1: 60, sperrzeit2: 120, solvers: new IOptimizerSolver[] { new OrtToolsSolver() });

        sut.Schaltkontingent[0] = 2.0;
        sut.Schaltkontingent[1] = 2.0;

        sut.Schaltzustand[0, 0] = 0.0;
        sut.Schaltzustand[0, 1] = 1.0;
        sut.Schaltzustand[1, 0] = 1.0;
        sut.Schaltzustand[1, 1] = 0.0;

        sut.Postprocessing(now);

        Assert.Equal(1.0, sut.Schaltkontingent[0], 12);
        Assert.Equal(1.0, sut.Schaltkontingent[1], 12);
        Assert.Equal(now, sut.Schaltzeit[1]);
    }

    [Fact]
    public void UpdateVerteilung_UpdatesFaktorInPythonOrder()
    {
        var sut = new Optimierer(3, sperrzeit1: 60, sperrzeit2: 120, solvers: new IOptimizerSolver[] { new OrtToolsSolver() });

        sut.UpdateVerteilung(new[] { 2.0, 1.0, 0.0 }, new[] { 4.0, 1.0, 1.0 });

        Assert.Equal(0.8, sut.Faktor[0], 12);
        Assert.Equal(0.7, sut.Faktor[1], 12);
        Assert.Equal(0.9, sut.Faktor[2], 12);
    }

    [Fact]
    public void UpdateVerteilungMittelsEnergie_UsesDeltasFromCounters()
    {
        var sut = new Optimierer(2, sperrzeit1: 60, sperrzeit2: 120, solvers: new IOptimizerSolver[] { new OrtToolsSolver() });

        sut.UpdateVerteilungMittelsEnergie(new[] { 10.0, 0.0 }, new[] { 20.0, 10.0 });
        sut.UpdateVerteilungMittelsEnergie(new[] { 15.0, 5.0 }, new[] { 30.0, 20.0 });

        Assert.Equal(5.0, sut.Verteilung[0, 0], 12);
        Assert.Equal(5.0, sut.Verteilung[1, 0], 12);
        Assert.Equal(10.0, sut.Verteilung[0, 1], 12);
        Assert.Equal(10.0, sut.Verteilung[1, 1], 12);
    }

    [Fact]
    public void Run_ReturnsExpectedSwitchStates_WithOrtToolsSolver()
    {
        var now = new DateTimeOffset(2026, 2, 27, 10, 0, 0, TimeSpan.Zero);
        var sut = new Optimierer(3, sperrzeit1: 60, sperrzeit2: 120, solvers: new IOptimizerSolver[] { new OrtToolsSolver() });

        var result = sut.Run(6.0, new[] { 4.0, 3.0, 2.0 }, now);

        Assert.Equal(new[] { 1.0, 0.0, 1.0 }, result.Schaltzustand);
        Assert.Equal(new[] { 4.0, 0.0, 2.0 }, result.ResOpt);

        Assert.Equal(-0.968, sut.Schaltkontingent[0], 12);
        Assert.Equal(0.032, sut.Schaltkontingent[1], 12);
        Assert.Equal(-0.968, sut.Schaltkontingent[2], 12);
    }

    [Fact]
    public void Run_ReplaysCsvRows_WithValidBounds()
    {
        var csvPath = Path.Combine(AppContext.BaseDirectory, "Testdaten.csv");
        Assert.True(File.Exists(csvPath), $"CSV not found at '{csvPath}'.");

        var lines = File.ReadLines(csvPath).Skip(1).Where(static l => !string.IsNullOrWhiteSpace(l));
        var now = new DateTimeOffset(2026, 2, 27, 0, 0, 0, TimeSpan.Zero);
        var sut = new Optimierer(7, sperrzeit1: 60, sperrzeit2: 120, solvers: new IOptimizerSolver[] { new OrtToolsSolver() });

        var rowCount = 0;
        foreach (var line in lines)
        {
            var parts = line.Split(';');
            Assert.Equal(8, parts.Length);

            var verbrauch = new double[7];
            for (var i = 0; i < 7; i++)
            {
                verbrauch[i] = Parse(parts[i]);
                Assert.True(verbrauch[i] >= 0.0, $"Verbrauch muss >= 0 sein (row {rowCount + 1}, col V{i + 1}).");
            }

            var pv = Parse(parts[7]);
            Assert.True(pv >= 0.0, $"PV muss >= 0 sein (row {rowCount + 1}).");

            var result = sut.Run(pv, verbrauch, now.AddMinutes(rowCount));

            Assert.Equal(7, result.Schaltzustand.Length);
            Assert.Equal(7, result.ResOpt.Length);

            var assigned = 0.0;
            for (var i = 0; i < 7; i++)
            {
                Assert.True(result.Schaltzustand[i] is 0.0 or 1.0, $"Schaltzustand muss 0 oder 1 sein (row {rowCount + 1}, user {i + 1}).");
                Assert.True(result.ResOpt[i] >= -1e-9, $"ResOpt darf nicht negativ sein (row {rowCount + 1}, user {i + 1}).");
                Assert.True(result.ResOpt[i] <= verbrauch[i] + 1e-9, $"ResOpt darf Verbrauch nicht ueberschreiten (row {rowCount + 1}, user {i + 1}).");
                assigned += result.ResOpt[i];
            }

            Assert.True(assigned <= pv + 1e-6, $"Summierte Zuweisung darf PV nicht ueberschreiten (row {rowCount + 1}).");
            rowCount++;
        }

        Assert.True(rowCount > 0, "CSV muss mindestens eine Datenzeile enthalten.");
    }

    [Fact]
    public void Run_RebuildsPythonAufrufOptimiererHarness()
    {
        const int simDauer = 5760 * 2;
        const int userCount = 7;

        var csvPath = Path.Combine(AppContext.BaseDirectory, "Testdaten.csv");
        Assert.True(File.Exists(csvPath), $"CSV not found at '{csvPath}'.");

        var lines = File.ReadLines(csvPath)
            .Skip(1)
            .Where(static l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.True(lines.Length >= simDauer, $"CSV must have at least {simDauer} data rows.");

        var resOpt = new double[userCount];
        var pvVerbrauchEnergieStand = new double[userCount];
        var verbrauchEnergieStand = new double[userCount];
        var schaltzustaende = new double[simDauer, userCount];

        var zeitstempel = new DateTimeOffset(2021, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var step = TimeSpan.FromSeconds(15);

        var sut = new Optimierer(userCount, sperrzeit1: 5 * 60, sperrzeit2: 60, solvers: new IOptimizerSolver[] { new OrtToolsSolver() });
        sut.UpdateVerteilungMittelsEnergie(pvVerbrauchEnergieStand, verbrauchEnergieStand);

        for (var j = 0; j < simDauer; j++)
        {
            var parts = lines[j].Split(';');
            Assert.Equal(8, parts.Length);

            var verbrauch = new double[userCount];
            for (var i = 0; i < userCount; i++)
            {
                verbrauch[i] = Parse(parts[i]);
            }

            var pv = Parse(parts[7]);

            if (pv > 0)
            {
                int xxx = 0;
            }

            zeitstempel = zeitstempel.Add(step);
            var result = sut.Run(pv, verbrauch, zeitstempel);

            for (var i = 0; i < userCount; i++)
            {
                schaltzustaende[j, i] = result.Schaltzustand[i];
                resOpt[i] = result.ResOpt[i];
                pvVerbrauchEnergieStand[i] += resOpt[i];
                verbrauchEnergieStand[i] += verbrauch[i];
            }

            sut.UpdateVerteilungMittelsEnergie(pvVerbrauchEnergieStand, verbrauchEnergieStand);
        }

        Assert.Equal(simDauer, schaltzustaende.GetLength(0));
        Assert.Equal(userCount, schaltzustaende.GetLength(1));
        Assert.All(pvVerbrauchEnergieStand, static v => Assert.True(v >= -1e-9));
        Assert.All(verbrauchEnergieStand, static v => Assert.True(v >= -1e-9));
    }

    private static double Parse(string value)
    {
        return double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }
}
