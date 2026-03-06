namespace SoWeiT.Optimizer.Persistence.History.Data;

public sealed class OptimizerRequestUserEntry
{
    public long Id { get; set; }

    public long RequestEntryId { get; set; }

    public int UserIndex { get; set; }

    public long CustomerId { get; set; }

    public double RequiredPowerWatt { get; set; }

    public bool IsSwitchAllowed { get; set; }

    public OptimizerRequestEntry Request { get; set; } = null!;

    public CustomerEntry Customer { get; set; } = null!;
}

