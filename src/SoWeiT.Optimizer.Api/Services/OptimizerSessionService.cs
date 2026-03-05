using System.Collections.Concurrent;
using SoWeiT.Optimizer;
using SoWeiT.Optimizer.Api.Contracts;
using SoWeiT.Optimizer.Api.Models;
using SoWeiT.Optimizer.Api.Persistence;
using SoWeiT.Optimizer.Solver;

namespace SoWeiT.Optimizer.Api.Services;

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
        _historyStore.CreateSession(sessionId, request, DateTime.UtcNow);
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
                eventType,
                requestTimestamp,
                availablePvPowerWatt,
                users));
        Touch(sessionId);
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
}
