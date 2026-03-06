using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public interface IOptimizerSessionRepository
{
    OptimizerSessionEntry? FindById(Guid sessionId);

    bool Exists(Guid sessionId);

    void Add(OptimizerSessionEntry session);
}

