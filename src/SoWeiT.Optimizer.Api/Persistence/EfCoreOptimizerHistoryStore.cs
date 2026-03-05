using SoWeiT.Optimizer.Api.Contracts;
using SoWeiT.Optimizer.Api.Data;

namespace SoWeiT.Optimizer.Api.Persistence;

public sealed record OptimizerRequestUserLog(int UserIndex, double RequiredPowerWatt, bool IsSwitchAllowed);

public sealed record OptimizerRequestLog(
    string RequestType,
    DateTimeOffset RequestTimestamp,
    double? AvailablePvPowerWatt,
    IReadOnlyList<OptimizerRequestUserLog>? Users = null);

public interface IOptimizerHistoryStore
{
    void CreateSession(Guid sessionId, CreateOptimizerSessionRequest request, DateTime createdAtUtc);

    void MarkSessionEnded(Guid sessionId, DateTime endedAtUtc);

    void AppendRequest(Guid sessionId, OptimizerRequestLog request);
}

public sealed class EfCoreOptimizerHistoryStore : IOptimizerHistoryStore
{
    private readonly IOptimizerUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<EfCoreOptimizerHistoryStore> _logger;

    public EfCoreOptimizerHistoryStore(
        IOptimizerUnitOfWorkFactory unitOfWorkFactory,
        ILogger<EfCoreOptimizerHistoryStore> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
    }

    public void CreateSession(Guid sessionId, CreateOptimizerSessionRequest request, DateTime createdAtUtc)
    {
        _logger.LogInformation("Create session history start: SessionId={SessionId}", sessionId);
        using var uow = _unitOfWorkFactory.Create();

        var entity = uow.Sessions.FindById(sessionId);
        if (entity is null)
        {
            entity = new OptimizerSessionEntry
            {
                SessionId = sessionId,
                CreatedAtUtc = createdAtUtc,
                EndedAtUtc = null,
                N = request.N,
                Sperrzeit1 = request.Sperrzeit1,
                Sperrzeit2 = request.Sperrzeit2,
                UseOrTools = request.UseOrTools,
                UseGreedyFallback = request.UseGreedyFallback
            };
            uow.Sessions.Add(entity);
        }
        else
        {
            entity.N = request.N;
            entity.Sperrzeit1 = request.Sperrzeit1;
            entity.Sperrzeit2 = request.Sperrzeit2;
            entity.UseOrTools = request.UseOrTools;
            entity.UseGreedyFallback = request.UseGreedyFallback;
            entity.CreatedAtUtc = createdAtUtc;
            entity.EndedAtUtc = null;
        }

        uow.SaveChanges();
        _logger.LogInformation("Create session history done: SessionId={SessionId}", sessionId);
    }

    public void MarkSessionEnded(Guid sessionId, DateTime endedAtUtc)
    {
        _logger.LogInformation("Mark session end start: SessionId={SessionId}", sessionId);
        using var uow = _unitOfWorkFactory.Create();
        var session = uow.Sessions.FindById(sessionId);
        if (session is null)
        {
            _logger.LogWarning("Mark session end skipped; session not found: SessionId={SessionId}", sessionId);
            return;
        }

        session.EndedAtUtc = endedAtUtc;
        uow.SaveChanges();
        _logger.LogInformation("Mark session end done: SessionId={SessionId}", sessionId);
    }

    public void AppendRequest(Guid sessionId, OptimizerRequestLog request)
    {
        _logger.LogInformation("Request history append start: SessionId={SessionId}, RequestType={RequestType}", sessionId, request.RequestType);
        using var uow = _unitOfWorkFactory.Create();

        if (!uow.Sessions.Exists(sessionId))
        {
            _logger.LogWarning("Request history append skipped; session not found: SessionId={SessionId}", sessionId);
            return;
        }

        var requestEntity = new OptimizerRequestEntry
        {
            SessionId = sessionId,
            RequestType = request.RequestType,
            RequestTimestamp = request.RequestTimestamp,
            CreatedAtUtc = DateTime.UtcNow,
            AvailablePvPowerWatt = request.AvailablePvPowerWatt
        };

        if (request.Users is { Count: > 0 })
        {
            foreach (var user in request.Users)
            {
                requestEntity.Users.Add(new OptimizerRequestUserEntry
                {
                    UserIndex = user.UserIndex,
                    RequiredPowerWatt = user.RequiredPowerWatt,
                    IsSwitchAllowed = user.IsSwitchAllowed
                });
            }
        }

        uow.Requests.Add(requestEntity);
        uow.SaveChanges();
        _logger.LogInformation(
            "Request history append done: SessionId={SessionId}, RequestType={RequestType}, RequestId={RequestId}, UserEntries={UserEntries}",
            sessionId,
            request.RequestType,
            requestEntity.Id,
            requestEntity.Users.Count);
    }
}
