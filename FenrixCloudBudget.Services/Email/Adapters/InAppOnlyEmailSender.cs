using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;

namespace FenrixCloudBudget.Services.Email.Adapters;

/// <summary>
/// Default adapter: sends no email. Budget/reminder alerts surface via in-app + local
/// device notifications only. Works fully offline with no backend and no credentials.
/// </summary>
public sealed class InAppOnlyEmailSender : IEmailSender
{
    public EmailMethod Method => EmailMethod.InAppAndDeviceOnly;

    public IReadOnlyList<EmailFieldSpec> FieldSchema => Array.Empty<EmailFieldSpec>();

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        => Task.FromResult(EmailResult.Fail("Email is disabled (In-app & device only). Pick an email method in Settings to enable email."));

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default)
        => Task.FromResult(EmailResult.Fail("No email method configured."));
}
