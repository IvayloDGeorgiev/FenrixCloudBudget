using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Models;

namespace FenrixCloudBudget.Core.Interfaces;

/// <summary>
/// One outbound-email transport. Adapters: SMTP, Resend, SendGrid, SES, Postmark,
/// Azure Communication Services, plus a no-op in-app/device-only adapter.
/// The Settings UI renders each adapter's fields dynamically from FieldSchema.
/// </summary>
public interface IEmailSender
{
    EmailMethod Method { get; }

    /// <summary>Field definitions the Settings UI renders for this method (some marked secret).</summary>
    IReadOnlyList<EmailFieldSpec> FieldSchema { get; }

    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default);

    /// <summary>Powers the "Send test email" button.</summary>
    Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default);
}

/// <summary>Describes one configurable field for an email adapter (drives dynamic Settings form).</summary>
public record EmailFieldSpec(string Key, string Label, bool IsSecret = false, bool Required = true, string? Placeholder = null, string[]? Choices = null);

/// <summary>Resolves the active IEmailSender from the saved EmailConfig.</summary>
public interface IEmailSenderFactory
{
    IEmailSender Resolve(EmailMethod method);
}
