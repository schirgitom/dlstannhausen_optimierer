using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public interface IOptimizerUnitOfWork : IDisposable
{
    OptimizerHistoryDbContext DbContext { get; }

    IOptimizerSessionRepository Sessions { get; }

    IOptimizerRequestRepository Requests { get; }

    void SaveChanges();
}

