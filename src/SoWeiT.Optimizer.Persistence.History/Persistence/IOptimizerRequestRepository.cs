using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Persistence.History.Persistence;

public interface IOptimizerRequestRepository
{
    void Add(OptimizerRequestEntry request);
}

