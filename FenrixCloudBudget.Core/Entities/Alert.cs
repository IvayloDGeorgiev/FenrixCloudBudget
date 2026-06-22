using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>A configured alert rule (currently budget threshold breaches).</summary>
public class Alert : EntityBase
{
    public int? BudgetId { get; set; }
    public Budget? Budget { get; set; }

    public int ThresholdPercent { get; set; }
    public NotificationChannel Channels { get; set; } = NotificationChannel.InApp | NotificationChannel.LocalDevice;
    public bool IsEnabled { get; set; } = true;

    /// <summary>Last period for which this threshold fired (prevents repeat sends within a period).</summary>
    public string? LastFiredPeriodKey { get; set; }
    public DateTimeOffset? LastFiredUtc { get; set; }
}
