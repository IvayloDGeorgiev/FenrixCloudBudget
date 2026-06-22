using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>Audit log of every notification fired — used for de-duplication and an audit trail.</summary>
public class NotificationDelivery : EntityBase
{
    public NotificationSourceType SourceType { get; set; }
    public int? SourceId { get; set; }              // Budget/Reminder id
    public NotificationChannel Channel { get; set; }
    public string? Recipient { get; set; }          // email address or device id
    public string? Subject { get; set; }
    public DeliveryResult Result { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset FiredUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Idempotency key used to suppress duplicate sends (e.g. "reminder:12:lead7:2026-06-22").</summary>
    public string? DedupeKey { get; set; }
}
