using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FenrixCloudBudget.Services.Email.Adapters;

/// <summary>
/// Custom SMTP adapter (MailKit). Works in local/desktop mode with the user's own mailbox
/// (self-hosted, Microsoft 365, etc.). Reads its settings from the active EmailConfig via
/// the injected <see cref="EmailAdapterContext"/>.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailAdapterContext _ctx;

    public SmtpEmailSender(EmailAdapterContext ctx) => _ctx = ctx;

    public EmailMethod Method => EmailMethod.CustomSmtp;

    public IReadOnlyList<EmailFieldSpec> FieldSchema => new[]
    {
        new EmailFieldSpec("host", "SMTP host", Placeholder: "smtp.example.com"),
        new EmailFieldSpec("port", "Port", Placeholder: "587"),
        new EmailFieldSpec("encryption", "Encryption", Required: false, Choices: new[] { "StartTls", "Ssl", "None" }),
        new EmailFieldSpec("username", "Username"),
        new EmailFieldSpec("password", "Password", IsSecret: true),
        new EmailFieldSpec("fromName", "From name", Required: false),
        new EmailFieldSpec("fromAddress", "From address", Placeholder: "alerts@example.com")
    };

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            var o = await _ctx.GetOptionsAsync(ct);
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(o.GetValueOrDefault("fromName") ?? "FenrixCloudBudget", o["fromAddress"]));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

            var encryption = o.GetValueOrDefault("encryption") switch
            {
                "Ssl" => SecureSocketOptions.SslOnConnect,
                "None" => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls
            };

            using var client = new SmtpClient();
            await client.ConnectAsync(o["host"], int.Parse(o.GetValueOrDefault("port") ?? "587"), encryption, ct);
            if (o.TryGetValue("username", out var user) && !string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, await _ctx.GetSecretAsync("password", ct), ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            return EmailResult.Ok();
        }
        catch (Exception ex)
        {
            return EmailResult.Fail(ex.Message);
        }
    }

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default)
        => SendAsync(new EmailMessage(to, "FenrixCloudBudget test email",
            "<p>Your SMTP configuration works. 🎉</p>", "Your SMTP configuration works."), ct);
}
