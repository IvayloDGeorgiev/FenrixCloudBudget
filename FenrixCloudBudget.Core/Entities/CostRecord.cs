using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>A time-series cost row used to drive charts. One row per service per day (or per sync grain).</summary>
public class CostRecord : EntityBase
{
    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public ServiceSource Source { get; set; } = ServiceSource.Connected;

    /// <summary>When this figure was pulled from the provider (for "last synced X ago" labels).</summary>
    public DateTimeOffset SyncedUtc { get; set; } = DateTimeOffset.UtcNow;
}
