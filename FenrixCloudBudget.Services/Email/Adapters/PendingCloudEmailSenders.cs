using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;

namespace FenrixCloudBudget.Services.Email.Adapters;

// These two adapters expose their field schemas (so the Settings UI is complete) but their
// send path needs a provider SDK that isn't referenced yet, to avoid bloating dependencies
// until the feature is switched on. Wire the package + implementation in Phase 2/5.

/// <summary>
/// Amazon SES adapter (cheapest at scale). Add the AWSSDK.SimpleEmailV2 package and
/// implement SendAsync via AmazonSimpleEmailServiceV2Client.SendEmailAsync.
/// </summary>
public sealed class SesEmailSender : IEmailSender
{
    private readonly EmailAdapterContext _ctx;
    public SesEmailSender(EmailAdapterContext ctx) => _ctx = ctx;

    public EmailMethod Method => EmailMethod.AmazonSes;
    public IReadOnlyList<EmailFieldSpec> FieldSchema => new[]
    {
        new EmailFieldSpec("accessKeyId", "Access key ID"),
        new EmailFieldSpec("secretAccessKey", "Secret access key", IsSecret: true),
        new EmailFieldSpec("region", "Region", Placeholder: "eu-west-1"),
        new EmailFieldSpec("fromAddress", "From address (verified domain)")
    };

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        => Task.FromResult(EmailResult.Fail("Amazon SES adapter not yet wired — add AWSSDK.SimpleEmailV2 and implement (Phase 2/5)."));

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default) => SendAsync(default!, ct);
}

/// <summary>
/// Azure Communication Services adapter. Add the Azure.Communication.Email package and
/// implement SendAsync via EmailClient(connectionString).SendAsync.
/// </summary>
public sealed class AzureCommunicationEmailSender : IEmailSender
{
    private readonly EmailAdapterContext _ctx;
    public AzureCommunicationEmailSender(EmailAdapterContext ctx) => _ctx = ctx;

    public EmailMethod Method => EmailMethod.AzureCommunicationServices;
    public IReadOnlyList<EmailFieldSpec> FieldSchema => new[]
    {
        new EmailFieldSpec("connectionString", "Connection string", IsSecret: true),
        new EmailFieldSpec("fromAddress", "From address")
    };

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        => Task.FromResult(EmailResult.Fail("Azure Communication Services adapter not yet wired — add Azure.Communication.Email and implement (Phase 2/5)."));

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default) => SendAsync(default!, ct);
}

/// <summary>
/// FenrixCloud managed (SaaS): the hosted backend sends on the user's behalf — zero config.
/// Implemented in Phase 5 once the API exists.
/// </summary>
public sealed class FenrixManagedEmailSender : IEmailSender
{
    public EmailMethod Method => EmailMethod.FenrixCloudManaged;
    public IReadOnlyList<EmailFieldSpec> FieldSchema => Array.Empty<EmailFieldSpec>();

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        => Task.FromResult(EmailResult.Fail("Managed email is available in SaaS mode (Phase 5)."));

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default) => SendAsync(default!, ct);
}
