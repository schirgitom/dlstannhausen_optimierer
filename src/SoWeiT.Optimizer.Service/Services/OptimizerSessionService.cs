using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoWeiT.Optimizer;
using SoWeiT.Optimizer.Models;
using SoWeiT.Optimizer.Persistence.History.Persistence;
using SoWeiT.Optimizer.Persistence.Redis.Models;
using SoWeiT.Optimizer.Persistence.Redis.Persistence;
using SoWeiT.Optimizer.Service.Contracts;
using SoWeiT.Optimizer.Solver;

namespace SoWeiT.Optimizer.Service.Services;

public sealed class OptimizerSessionService
{
    private readonly ConcurrentDictionary<Guid, Optimierer> _cache = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAccessUtc = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OptimizerSessionService> _logger;
    private readonly IOptimizerStateStore _stateStore;
    private readonly IOptimizerHistoryStore _historyStore;
    private readonly TimeSpan _inactivityTimeout;

    public OptimizerSessionService(
        ILoggerFactory loggerFactory,
        ILogger<OptimizerSessionService> logger,
        IOptimizerStateStore stateStore,
        IOptimizerHistoryStore historyStore,
        IConfiguration configuration)
    {
        _loggerFactory = loggerFactory;
        _logger = logger;
        _stateStore = stateStore;
        _historyStore = historyStore;

        var timeoutMinutes = configuration.GetValue<int?>("OptimizerStateStore:SessionTtlMinutes") ?? 720;
        _inactivityTimeout = TimeSpan.FromMinutes(timeoutMinutes);
    }

    public Guid Create(CreateOptimizerSessionRequest request)
    {
        _logger.LogInformation(
            "Create session requested: N={N}, Sperrzeit1={Sperrzeit1}, Sperrzeit2={Sperrzeit2}, UseOrTools={UseOrTools}, UseGreedyFallback={UseGreedyFallback}",
            request.N,
            request.Sperrzeit1,
            request.Sperrzeit2,
            request.UseOrTools,
            request.UseGreedyFallback);

        var optimizer = CreateOptimizer(request.N, request.Sperrzeit1, request.Sperrzeit2, request.UseOrTools, request.UseGreedyFallback);
        var sessionId = Guid.NewGuid();

        if (!_cache.TryAdd(sessionId, optimizer))
        {
            throw new InvalidOperationException("Could not create optimizer session.");
        }

        TouchLocal(sessionId);
        Persist(sessionId, optimizer, request.UseOrTools, request.UseGreedyFallback);
        _historyStore.CreateSession(
            sessionId,
            new OptimizerSessionConfig(
                request.N,
                request.Sperrzeit1,
                request.Sperrzeit2,
                request.UseOrTools,
                request.UseGreedyFallback),
            DateTime.UtcNow);
        _historyStore.AppendRequest(
            sessionId,
            new OptimizerRequestLog(
                "session_created",
                DateTimeOffset.UtcNow,
                optimizer.Erzeugung));
        _logger.LogInformation("Session created: {SessionId}", sessionId);
        return sessionId;
    }

    public bool TryGet(Guid sessionId, out Optimierer? optimizer)
    {
        if (IsExpired(sessionId))
        {
            _logger.LogWarning("Session expired by inactivity: {SessionId}", sessionId);
            _historyStore.AppendRequest(
                sessionId,
                new OptimizerRequestLog(
                    "session_expired",
                    DateTimeOffset.UtcNow,
                    _cache.TryGetValue(sessionId, out var cachedOptimizer) ? cachedOptimizer?.Erzeugung : null));
            _historyStore.MarkSessionEnded(sessionId, DateTime.UtcNow);
            Invalidate(sessionId);
            optimizer = null;
            return false;
        }

        if (_cache.TryGetValue(sessionId, out optimizer))
        {
            _logger.LogInformation("Session hit in memory cache: {SessionId}", sessionId);
            Touch(sessionId);
            return true;
        }

        if (!_stateStore.TryLoad(sessionId, out var persisted) || persisted is null)
        {
            _logger.LogWarning("Session not found in store: {SessionId}", sessionId);
            optimizer = null;
            return false;
        }

        optimizer = CreateOptimizer(
            persisted.Snapshot.N,
            persisted.Snapshot.Sperrzeit1,
            persisted.Snapshot.Sperrzeit2,
            persisted.UseOrTools,
            persisted.UseGreedyFallback);

        optimizer.ApplySnapshot(persisted.Snapshot);
        _cache.TryAdd(sessionId, optimizer);
        Touch(sessionId);
        _logger.LogInformation("Session restored from Redis: {SessionId}", sessionId);
        return true;
    }

    public bool Delete(Guid sessionId)
    {
        if (!TryGet(sessionId, out var optimizer) || optimizer is null)
        {
            return false;
        }

        Invalidate(sessionId);
        _historyStore.AppendRequest(
            sessionId,
            new OptimizerRequestLog(
                "session_deleted",
                DateTimeOffset.UtcNow,
                optimizer.Erzeugung));
        _historyStore.MarkSessionEnded(sessionId, DateTime.UtcNow);
        _logger.LogInformation("Session deleted: {SessionId}", sessionId);
        return true;
    }

    public void PersistMutation(
        Guid sessionId,
        Optimierer optimizer,
        string eventType,
        DateTimeOffset requestTimestamp,
        double? availablePvPowerWatt,
        double? consumedPowerWatt = null,
        double? totalRequiredPowerWatt = null,
        IReadOnlyList<OptimizerRequestUserLog>? users = null)
    {
        if (!_stateStore.TryLoad(sessionId, out var persisted) || persisted is null)
        {
            throw new InvalidOperationException($"Cannot persist unknown session {sessionId}");
        }

        Persist(sessionId, optimizer, persisted.UseOrTools, persisted.UseGreedyFallback);
        _historyStore.AppendRequest(
            sessionId,
            new OptimizerRequestLog(
                RequestType: eventType,
                RequestTimestamp: requestTimestamp,
                AvailablePvPowerWatt: availablePvPowerWatt,
                ConsumedPowerWatt: consumedPowerWatt,
                TotalRequiredPowerWatt: totalRequiredPowerWatt,
                Users: users));
        Touch(sessionId);
    }

    public bool TryPrepareData(Guid sessionId, PrepareDataRequest request, out SolverInputData? preparedData)
    {
        preparedData = null;
        if (!TryGet(sessionId, out var optimizer) || optimizer is null)
        {
            return false;
        }

        preparedData = optimizer.PrepareData(request.Verbrauch);
        return true;
    }

    public bool TryPreprocessing(
        Guid sessionId,
        PreprocessingRequest request,
        out double[]? preprocessedResult,
        out string? validationError)
    {
        preprocessedResult = null;
        validationError = null;

        if (!TryGet(sessionId, out var optimizer) || optimizer is null)
        {
            return false;
        }

        if (!TryExtractUserPower(request.Nutzer, out var customers, out var requiredPowerWatt, out validationError))
        {
            return true;
        }

        preprocessedResult = optimizer.Preprocessing(request.Zeitstempel, requiredPowerWatt);
        var preprocessingUsers = BuildPreprocessingUserLogs(optimizer, customers, requiredPowerWatt, preprocessedResult);
        PersistMutation(
            sessionId,
            optimizer,
            "preprocessing",
            request.Zeitstempel,
            optimizer.Erzeugung,
            CalculateConsumedPower(preprocessingUsers),
            CalculateTotalRequiredPower(preprocessingUsers),
            preprocessingUsers);
        return true;
    }

    public bool TryPostprocessing(Guid sessionId, PostprocessingRequest request)
    {
        if (!TryGet(sessionId, out var optimizer) || optimizer is null)
        {
            return false;
        }

        optimizer.Postprocessing(request.Zeitstempel);
        PersistMutation(
            sessionId,
            optimizer,
            "postprocessing",
            request.Zeitstempel,
            optimizer.Erzeugung);
        return true;
    }

    public bool TryRun(Guid sessionId, RunRequest request, out RunResponse? response, out string? validationError)
    {
        response = null;
        validationError = null;

        if (!TryGet(sessionId, out var optimizer) || optimizer is null)
        {
            return false;
        }

        if (!TryExtractUserPower(request.Nutzer, out var customers, out var requiredPowerWatt, out validationError))
        {
            return true;
        }

        var result = optimizer.Run(request.PvErzeugungWatt, requiredPowerWatt, request.Zeitstempel);
        var pvVerbrauchEnergieStand = optimizer.PvVerbrauchEnergieStand?.ToArray() ?? new double[optimizer.N];
        var verbrauchEnergieStand = optimizer.VerbrauchEnergieStand?.ToArray() ?? new double[optimizer.N];
        for (var i = 0; i < optimizer.N; i++)
        {
            pvVerbrauchEnergieStand[i] += result.ResOpt[i];
            verbrauchEnergieStand[i] += requiredPowerWatt[i];
        }

        optimizer.UpdateVerteilungMittelsEnergie(pvVerbrauchEnergieStand, verbrauchEnergieStand);
        var runUsers = BuildRunUserLogs(optimizer, customers, requiredPowerWatt, result.Schaltzustand);
        PersistMutation(
            sessionId,
            optimizer,
            "run",
            request.Zeitstempel,
            request.PvErzeugungWatt,
            CalculateConsumedPower(runUsers),
            CalculateTotalRequiredPower(runUsers),
            runUsers);
        response = new RunResponse(result.Schaltzustand, result.ResOpt, result.ResOpt);
        return true;
    }

    public bool TryGetState(Guid sessionId, out OptimizerStateResponse? state)
    {
        state = null;
        if (!TryGet(sessionId, out var optimizer) || optimizer is null)
        {
            return false;
        }

        state = new OptimizerStateResponse(
            optimizer.N,
            optimizer.Sperrzeit1,
            optimizer.Sperrzeit2,
            optimizer.Erzeugung,
            optimizer.Faktor.ToArray(),
            ToJagged(optimizer.Verteilung),
            optimizer.Schaltkontingent.ToArray(),
            optimizer.Schaltzeit.ToArray(),
            ToJagged(optimizer.Schaltzustand),
            optimizer.PvVerbrauchEnergieStand?.ToArray(),
            optimizer.VerbrauchEnergieStand?.ToArray());
        return true;
    }

    private void Persist(
        Guid sessionId,
        Optimierer optimizer,
        bool useOrTools,
        bool useGreedyFallback)
    {
        var snapshot = optimizer.CreateSnapshot();
        var persisted = new PersistedOptimizerSession
        {
            SessionId = sessionId,
            UseOrTools = useOrTools,
            UseGreedyFallback = useGreedyFallback,
            Snapshot = snapshot
        };

        _stateStore.Save(persisted);
    }

    private void Touch(Guid sessionId)
    {
        TouchLocal(sessionId);
        _stateStore.Touch(sessionId);
    }

    private void TouchLocal(Guid sessionId)
    {
        _lastAccessUtc[sessionId] = DateTimeOffset.UtcNow;
    }

    private bool IsExpired(Guid sessionId)
    {
        if (!_lastAccessUtc.TryGetValue(sessionId, out var lastAccessUtc))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - lastAccessUtc > _inactivityTimeout;
    }

    private void Invalidate(Guid sessionId)
    {
        _cache.TryRemove(sessionId, out _);
        _lastAccessUtc.TryRemove(sessionId, out _);
        _stateStore.Delete(sessionId);
    }

    private Optimierer CreateOptimizer(int n, int sperrzeit1, int sperrzeit2, bool useOrTools, bool useGreedyFallback)
    {
        var solvers = new List<IOptimizerSolver>();
        if (useOrTools)
        {
            solvers.Add(new OrtToolsSolver());
        }

        if (useGreedyFallback)
        {
            solvers.Add(new GreedyFallbackSolver());
        }

        if (solvers.Count == 0)
        {
            throw new InvalidOperationException("At least one solver must be enabled.");
        }

        var optimizerLogger = _loggerFactory.CreateLogger<Optimierer>();
        return new Optimierer(n, sperrzeit1, sperrzeit2, optimizerLogger, solvers);
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

    private static IReadOnlyList<OptimizerRequestUserLog> BuildPreprocessingUserLogs(
        Optimierer optimizer,
        string[] customers,
        double[] requiredPowerWatt,
        double[] preprocessedResult)
    {
        var users = new List<OptimizerRequestUserLog>(requiredPowerWatt.Length);
        for (var i = 0; i < requiredPowerWatt.Length; i++)
        {
            var isSwitchAllowed = requiredPowerWatt[i] <= 0.0 || preprocessedResult[i] > 0.0;
            users.Add(BuildUserLog(optimizer, i, customers[i], requiredPowerWatt[i], isSwitchAllowed, shouldSwitch: null));
        }

        return users;
    }

    private static IReadOnlyList<OptimizerRequestUserLog> BuildRunUserLogs(
        Optimierer optimizer,
        string[] customers,
        double[] requiredPowerWatt,
        double[] switchState)
    {
        var users = new List<OptimizerRequestUserLog>(requiredPowerWatt.Length);
        for (var i = 0; i < requiredPowerWatt.Length; i++)
        {
            var shouldSwitch = switchState[i] > 0.0;
            users.Add(BuildUserLog(optimizer, i, customers[i], requiredPowerWatt[i], shouldSwitch, shouldSwitch));
        }

        return users;
    }

    private static OptimizerRequestUserLog BuildUserLog(
        Optimierer optimizer,
        int index,
        string customer,
        double requiredPowerWatt,
        bool isSwitchAllowed,
        bool? shouldSwitch)
    {
        return new OptimizerRequestUserLog(
            UserIndex: index,
            Customer: customer,
            RequiredPowerWatt: requiredPowerWatt,
            IsSwitchAllowed: isSwitchAllowed,
            FairnessFactor: optimizer.Faktor[index],
            SwitchBudget: optimizer.Schaltkontingent[index],
            ShouldSwitch: shouldSwitch);
    }

    private static double CalculateConsumedPower(IReadOnlyList<OptimizerRequestUserLog> users)
    {
        return users
            .Where(x => x.IsSwitchAllowed && x.RequiredPowerWatt > 0.0)
            .Sum(x => x.RequiredPowerWatt);
    }

    private static double CalculateTotalRequiredPower(IReadOnlyList<OptimizerRequestUserLog> users)
    {
        return users
            .Where(x => x.RequiredPowerWatt > 0.0)
            .Sum(x => x.RequiredPowerWatt);
    }

    private static bool TryExtractUserPower(
        UserPowerRequest[]? nutzer,
        out string[] customers,
        out double[] requiredPowerWatt,
        out string? validationError)
    {
        if (nutzer is not { Length: > 0 })
        {
            customers = [];
            requiredPowerWatt = [];
            validationError = "Request.Nutzer muss gesetzt sein und mindestens einen Eintrag enthalten.";
            return false;
        }

        customers = new string[nutzer.Length];
        requiredPowerWatt = new double[nutzer.Length];
        for (var i = 0; i < nutzer.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(nutzer[i].Kunde))
            {
                validationError = $"Request.Nutzer[{i}].Kunde darf nicht leer sein.";
                return false;
            }

            customers[i] = nutzer[i].Kunde.Trim();
            requiredPowerWatt[i] = nutzer[i].VerbrauchWatt;
        }

        validationError = null;
        return true;
    }
}

