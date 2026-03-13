using Microsoft.Extensions.Logging;
using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public sealed class OptimizerRequestRepository : IOptimizerRequestRepository
{
    private readonly OptimizerHistoryDbContext _dbContext;
    private readonly ILogger<OptimizerRequestRepository> _logger;

    public OptimizerRequestRepository(
        OptimizerHistoryDbContext dbContext,
        ILogger<OptimizerRequestRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public void Add(OptimizerRequestEntry request)
    {
        _logger.LogDebug(
            "Add request entity: SessionId={SessionId}, RequestType={RequestType}, Timestamp={Timestamp}",
            request.SessionId,
            request.RequestType,
            request.RequestTimestamp);
        _dbContext.Requests.Add(request);
    }
}

