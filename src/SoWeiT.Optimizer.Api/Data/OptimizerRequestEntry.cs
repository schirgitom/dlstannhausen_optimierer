namespace SoWeiT.Optimizer.Api.Data;

public sealed class OptimizerRequestEntry
{
    public long Id { get; set; }

    public Guid SessionId { get; set; }

    public required string RequestType { get; set; }

    public DateTimeOffset RequestTimestamp { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public double? AvailablePvPowerWatt { get; set; }

    public OptimizerSessionEntry Session { get; set; } = null!;

    public ICollection<OptimizerRequestUserEntry> Users { get; set; } = new List<OptimizerRequestUserEntry>();
}
