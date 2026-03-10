namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public sealed record OptimizerRequestUserLog(
    int UserIndex,
    string Customer,
    double RequiredPowerWatt,
    bool IsSwitchAllowed,
    double? FairnessFactor = null,
    double? SwitchBudget = null,
    bool? ShouldSwitch = null);

public sealed record OptimizerSessionConfig(int N, int Sperrzeit1, int Sperrzeit2, bool UseOrTools, bool UseGreedyFallback);

public sealed record OptimizerRequestLog(
    string RequestType,
    DateTimeOffset RequestTimestamp,
    double? AvailablePvPowerWatt,
    double? ConsumedPowerWatt = null,
    double? TotalRequiredPowerWatt = null,
    IReadOnlyList<OptimizerRequestUserLog>? Users = null);

public interface IOptimizerHistoryStore
{
    void CreateSession(Guid sessionId, OptimizerSessionConfig sessionConfig, DateTime createdAtUtc);

    void MarkSessionEnded(Guid sessionId, DateTime endedAtUtc);

    void AppendRequest(Guid sessionId, OptimizerRequestLog request);
}

public enum OptimizerHistoryEventType
{
    SessionCreated = 1,
    SessionEnded = 2,
    RequestAppended = 3
}

public sealed record OptimizerHistoryEvent(
    OptimizerHistoryEventType EventType,
    Guid SessionId,
    OptimizerSessionConfig? SessionConfig = null,
    DateTime? CreatedAtUtc = null,
    DateTime? EndedAtUtc = null,
    OptimizerRequestLog? Request = null);
