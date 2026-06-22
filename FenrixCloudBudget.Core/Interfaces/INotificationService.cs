using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Interfaces;

/// <summary>
/// Single messaging layer for ALL outbound alerts: budget thresholds, reminders, OTP/invites.
/// In-app + local device work offline from Phase 1; email is gated on a configured IEmailSender.
/// Every send is recorded in NotificationDelivery for de-duplication and audit.
/// </summary>
public interface INotificationService
{
    Task NotifyInAppAsync(string title, string message, CancellationToken ct = default);

    Task NotifyLocalAsync(string title, string message, DateTimeOffset? at = null, CancellationToken ct = default);

    Task<bool> SendEmailAsync(string to, string templateKey, object data, CancellationToken ct = default);

    /// <summary>Fan out to all requested channels, honoring dedupe key + quiet hours.</summary>
    Task DispatchAsync(NotificationRequest request, CancellationToken ct = default);
}

public record NotificationRequest(
    NotificationSourceType SourceType,
    int? SourceId,
    NotificationChannel Channels,
    string Title,
    string Message,
    string? EmailTo = null,
    string? EmailTemplateKey = null,
    object? EmailData = null,
    string? DedupeKey = null);
