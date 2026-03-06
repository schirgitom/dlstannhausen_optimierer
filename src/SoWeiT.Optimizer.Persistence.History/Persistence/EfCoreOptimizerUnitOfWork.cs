using Microsoft.EntityFrameworkCore;
using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public sealed class EfCoreOptimizerUnitOfWork : IOptimizerUnitOfWork
{
    private readonly OptimizerHistoryDbContext _dbContext;

    public EfCoreOptimizerUnitOfWork(OptimizerHistoryDbContext dbContext)
    {
        _dbContext = dbContext;
        Sessions = new OptimizerSessionRepository(_dbContext);
        Requests = new OptimizerRequestRepository(_dbContext);
    }

    public OptimizerHistoryDbContext DbContext => _dbContext;

    public IOptimizerSessionRepository Sessions { get; }

    public IOptimizerRequestRepository Requests { get; }

    public void SaveChanges()
    {
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

public sealed class EfCoreOptimizerUnitOfWorkFactory : IOptimizerUnitOfWorkFactory
{
    private readonly IDbContextFactory<OptimizerHistoryDbContext> _dbContextFactory;

    public EfCoreOptimizerUnitOfWorkFactory(IDbContextFactory<OptimizerHistoryDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public IOptimizerUnitOfWork Create()
    {
        return new EfCoreOptimizerUnitOfWork(_dbContextFactory.CreateDbContext());
    }
}

