namespace SoWeiT.Optimizer.Persistence.History.Data;

public sealed class OptimizerSessionEntry
{
    public Guid SessionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public int N { get; set; }

    public int Sperrzeit1 { get; set; }

    public int Sperrzeit2 { get; set; }

    public bool UseOrTools { get; set; }

    public bool UseGreedyFallback { get; set; }

    public ICollection<OptimizerRequestEntry> Requests { get; set; } = new List<OptimizerRequestEntry>();
}

