using Microsoft.Extensions.Logging;
using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public sealed class EfCoreOptimizerHistoryStore : IOptimizerHistoryStore
{
    private static readonly HashSet<string> SkippedRequestTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "update_verteilung_mittels_energie"
    };

    private readonly IOptimizerUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<EfCoreOptimizerHistoryStore> _logger;

    public EfCoreOptimizerHistoryStore(
        IOptimizerUnitOfWorkFactory unitOfWorkFactory,
        ILogger<EfCoreOptimizerHistoryStore> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _logger = logger;
    }

    public void CreateSession(Guid sessionId, OptimizerSessionConfig sessionConfig, DateTime createdAtUtc)
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
                N = sessionConfig.N,
                Sperrzeit1 = sessionConfig.Sperrzeit1,
                Sperrzeit2 = sessionConfig.Sperrzeit2,
                UseOrTools = sessionConfig.UseOrTools,
                UseGreedyFallback = sessionConfig.UseGreedyFallback
            };
            uow.Sessions.Add(entity);
        }
        else
        {
            entity.N = sessionConfig.N;
            entity.Sperrzeit1 = sessionConfig.Sperrzeit1;
            entity.Sperrzeit2 = sessionConfig.Sperrzeit2;
            entity.UseOrTools = sessionConfig.UseOrTools;
            entity.UseGreedyFallback = sessionConfig.UseGreedyFallback;
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
        if (SkippedRequestTypes.Contains(request.RequestType))
        {
            _logger.LogDebug(
                "Request history append skipped by request type filter: SessionId={SessionId}, RequestType={RequestType}",
                sessionId,
                request.RequestType);
            return;
        }

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
            AvailablePvPowerWatt = request.AvailablePvPowerWatt,
            ConsumedPowerWatt = request.ConsumedPowerWatt,
            TotalRequiredPowerWatt = request.TotalRequiredPowerWatt
        };

        if (request.Users is { Count: > 0 })
        {
            var requestCustomerCache = new Dictionary<string, CustomerEntry>(StringComparer.Ordinal);
            foreach (var user in request.Users)
            {
                var customerNumber = user.Customer.Trim();
                if (customerNumber.Length == 0)
                {
                    throw new InvalidOperationException("Customer number must not be empty.");
                }

                if (!requestCustomerCache.TryGetValue(customerNumber, out var customerEntity))
                {
                    customerEntity = uow.DbContext.Customers.SingleOrDefault(x => x.CustomerNumber == customerNumber);
                    if (customerEntity is null)
                    {
                        throw new InvalidOperationException(
                            $"Customer with customer number '{customerNumber}' was not found.");
                    }

                    requestCustomerCache[customerNumber] = customerEntity;
                }

                requestEntity.Users.Add(new OptimizerRequestUserEntry
                {
                    UserIndex = user.UserIndex,
                    Customer = customerEntity,
                    RequiredPowerWatt = user.RequiredPowerWatt,
                    IsSwitchAllowed = user.IsSwitchAllowed,
                    FairnessFactor = user.FairnessFactor,
                    SwitchBudget = user.SwitchBudget,
                    ShouldSwitch = user.ShouldSwitch
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

