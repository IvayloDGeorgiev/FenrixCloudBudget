using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>A due-date reminder: client secret expiry, certificate expiry, or a custom item.</summary>
public class Reminder : EntityBase
{
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public ReminderType Type { get; set; }
    public ReminderStatus Status { get; set; } = ReminderStatus.Active;

    /// <summary>Optional link to a connected account (secret/cert types pre-fill expiry from it).</summary>
    public int? CloudAccountId { get; set; }
    public CloudAccount? CloudAccount { get; set; }

    /// <summary>Free-form target for Custom reminders.</summary>
    public string? CustomTarget { get; set; }

    public DateTimeOffset DueDate { get; set; }

    /// <summary>Days-before-due to fire alerts, comma-separated, e.g. "30,14,7,1".</summary>
    public string LeadTimesDays { get; set; } = "30,14,7,1";

    public NotificationChannel Channels { get; set; } = NotificationChannel.InApp | NotificationChannel.LocalDevice;

    /// <summary>Repeat interval in days (0 = one-off).</summary>
    public int RecurrenceDays { get; set; }

    public DateTimeOffset? SnoozedUntilUtc { get; set; }
    public DateTimeOffset? LastFiredUtc { get; set; }

    public IEnumerable<int> LeadTimes() =>
        (LeadTimesDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1)
            .Where(v => v >= 0);
}
