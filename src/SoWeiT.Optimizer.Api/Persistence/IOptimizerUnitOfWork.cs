namespace SoWeiT.Optimizer.Api.Persistence;

public interface IOptimizerUnitOfWork : IDisposable
{
    IOptimizerSessionRepository Sessions { get; }

    IOptimizerRequestRepository Requests { get; }

    void SaveChanges();
}
