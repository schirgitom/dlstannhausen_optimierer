using SoWeiT.Optimizer.Api.Data;

namespace SoWeiT.Optimizer.Api.Persistence;

public interface IOptimizerSessionRepository
{
    OptimizerSessionEntry? FindById(Guid sessionId);

    bool Exists(Guid sessionId);

    void Add(OptimizerSessionEntry session);
}
