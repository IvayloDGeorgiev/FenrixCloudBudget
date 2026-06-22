namespace FenrixCloudBudget.Core.Models;

/// <summary>A rendered email ready to send via any IEmailSender adapter.</summary>
public record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null);

/// <summary>Result of an outbound email attempt.</summary>
public record EmailResult(bool Success, string? ProviderMessageId = null, string? Error = null)
{
    public static EmailResult Ok(string? id = null) => new(true, id);
    public static EmailResult Fail(string error) => new(false, null, error);
}
