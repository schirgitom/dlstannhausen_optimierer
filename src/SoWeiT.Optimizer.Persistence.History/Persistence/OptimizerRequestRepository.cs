using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public sealed class OptimizerRequestRepository : IOptimizerRequestRepository
{
    private readonly OptimizerHistoryDbContext _dbContext;

    public OptimizerRequestRepository(OptimizerHistoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(OptimizerRequestEntry request)
    {
        _dbContext.Requests.Add(request);
    }
}

