using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public sealed class OptimizerSessionRepository : IOptimizerSessionRepository
{
    private readonly OptimizerHistoryDbContext _dbContext;

    public OptimizerSessionRepository(OptimizerHistoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public OptimizerSessionEntry? FindById(Guid sessionId)
    {
        return _dbContext.Sessions.Find(sessionId);
    }

    public bool Exists(Guid sessionId)
    {
        return _dbContext.Sessions.Any(x => x.SessionId == sessionId);
    }

    public void Add(OptimizerSessionEntry session)
    {
        _dbContext.Sessions.Add(session);
    }
}

