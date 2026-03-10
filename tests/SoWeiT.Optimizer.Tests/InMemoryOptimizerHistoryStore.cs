using SoWeiT.Optimizer.Persistence.History.Persistence;

namespace SoWeiT.Optimizer.Tests;

public sealed class InMemoryOptimizerHistoryStore : IOptimizerHistoryStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, SessionSnapshot> _sessions = new();
    private readonly Dictionary<Guid, List<OptimizerRequestLog>> _requests = new();

    public void CreateSession(Guid sessionId, OptimizerSessionConfig sessionConfig, DateTime createdAtUtc)
    {
        lock (_lock)
        {
            _sessions[sessionId] = new SessionSnapshot(sessionConfig, createdAtUtc, null);
            if (!_requests.ContainsKey(sessionId))
            {
                _requests[sessionId] = [];
            }
        }
    }

    public void MarkSessionEnded(Guid sessionId, DateTime endedAtUtc)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                _sessions[sessionId] = session with { EndedAtUtc = endedAtUtc };
            }
        }
    }

    public void AppendRequest(Guid sessionId, OptimizerRequestLog request)
    {
        lock (_lock)
        {
            if (!_requests.TryGetValue(sessionId, out var requestList))
            {
                requestList = [];
                _requests[sessionId] = requestList;
            }

            requestList.Add(request);
        }
    }

    public SessionSnapshot? TryGetSession(Guid sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
    }

    public IReadOnlyList<OptimizerRequestLog> GetRequests(Guid sessionId)
    {
        lock (_lock)
        {
            return _requests.TryGetValue(sessionId, out var requestList)
                ? requestList.ToArray()
                : [];
        }
    }

    public sealed record SessionSnapshot(
        OptimizerSessionConfig Config,
        DateTime CreatedAtUtc,
        DateTime? EndedAtUtc);
}
