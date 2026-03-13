using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public sealed class EfCoreOptimizerUnitOfWork : IOptimizerUnitOfWork
{
    private readonly OptimizerHistoryDbContext _dbContext;
    private readonly ILogger<EfCoreOptimizerUnitOfWork> _logger;

    public EfCoreOptimizerUnitOfWork(
        OptimizerHistoryDbContext dbContext,
        ILoggerFactory loggerFactory,
        ILogger<EfCoreOptimizerUnitOfWork> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        Sessions = new OptimizerSessionRepository(_dbContext, loggerFactory.CreateLogger<OptimizerSessionRepository>());
        Requests = new OptimizerRequestRepository(_dbContext, loggerFactory.CreateLogger<OptimizerRequestRepository>());
        _logger.LogDebug("Created EF Core unit of work instance");
    }

    public OptimizerHistoryDbContext DbContext => _dbContext;

    public IOptimizerSessionRepository Sessions { get; }

    public IOptimizerRequestRepository Requests { get; }

    public void SaveChanges()
    {
        _logger.LogDebug("Persisting unit of work changes");
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _logger.LogDebug("Disposing EF Core unit of work instance");
        _dbContext.Dispose();
    }
}

public sealed class EfCoreOptimizerUnitOfWorkFactory : IOptimizerUnitOfWorkFactory
{
    private readonly IDbContextFactory<OptimizerHistoryDbContext> _dbContextFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EfCoreOptimizerUnitOfWorkFactory> _logger;

    public EfCoreOptimizerUnitOfWorkFactory(
        IDbContextFactory<OptimizerHistoryDbContext> dbContextFactory,
        ILoggerFactory loggerFactory,
        ILogger<EfCoreOptimizerUnitOfWorkFactory> logger)
    {
        _dbContextFactory = dbContextFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public IOptimizerUnitOfWork Create()
    {
        _logger.LogDebug("Creating EF Core unit of work");
        return new EfCoreOptimizerUnitOfWork(
            _dbContextFactory.CreateDbContext(),
            _loggerFactory,
            _loggerFactory.CreateLogger<EfCoreOptimizerUnitOfWork>());
    }
}

