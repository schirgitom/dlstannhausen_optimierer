using SoWeiT.Optimizer.Persistence.Redis.Models;

namespace SoWeiT.Optimizer.Persistence.Redis.Persistence;

public interface IOptimizerStateStore
{
    bool TryLoad(Guid sessionId, out PersistedOptimizerSession? session);

    void Save(PersistedOptimizerSession session);

    void Touch(Guid sessionId);

    void Delete(Guid sessionId);
}

