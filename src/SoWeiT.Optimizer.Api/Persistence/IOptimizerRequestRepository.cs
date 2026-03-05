using SoWeiT.Optimizer.Api.Data;

namespace SoWeiT.Optimizer.Api.Persistence;

public interface IOptimizerRequestRepository
{
    void Add(OptimizerRequestEntry request);
}
