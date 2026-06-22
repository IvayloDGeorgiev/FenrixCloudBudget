using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>Shared email-service settings. One active row; credentials are stored encrypted and referenced.</summary>
public class EmailConfig : EntityBase
{
    public EmailMethod Method { get; set; } = EmailMethod.InAppAndDeviceOnly;
    public bool Enabled { get; set; }

    public string? FromName { get; set; }
    public string? FromAddress { get; set; }

    /// <summary>Reference into the secure credential store for this method's secret(s).</summary>
    public string? CredentialReference { get; set; }

    /// <summary>First-4-then-masked hint of the stored secret for display.</summary>
    public string? SecretHint { get; set; }

    /// <summary>Method-specific non-secret fields serialized as JSON (host, port, region, etc.).</summary>
    public string? OptionsJson { get; set; }

    /// <summary>Which channels are enabled overall (in-app / device / email).</summary>
    public NotificationChannel EnabledChannels { get; set; } =
        NotificationChannel.InApp | NotificationChannel.LocalDevice;

    public EmailConfigStatus Status { get; set; } = EmailConfigStatus.NotConfigured;
    public DateTimeOffset? LastTestedUtc { get; set; }

    // Quiet hours (local time, 24h). When From == To, quiet hours are disabled.
    public TimeOnly QuietHoursFrom { get; set; } = new(22, 0);
    public TimeOnly QuietHoursTo { get; set; } = new(7, 0);
}
