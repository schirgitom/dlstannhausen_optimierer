namespace SoWeiT.Optimizer.Persistence.History.Data;

public sealed class CustomerEntry
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? CustomerNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<OptimizerRequestUserEntry> RequestUsers { get; set; } = new List<OptimizerRequestUserEntry>();
}
