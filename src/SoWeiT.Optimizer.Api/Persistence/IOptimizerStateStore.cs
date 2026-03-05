using SoWeiT.Optimizer.Api.Models;

namespace SoWeiT.Optimizer.Api.Persistence;

public interface IOptimizerStateStore
{
    bool TryLoad(Guid sessionId, out PersistedOptimizerSession? session);

    void Save(PersistedOptimizerSession session);

    void Touch(Guid sessionId);

    void Delete(Guid sessionId);
}
