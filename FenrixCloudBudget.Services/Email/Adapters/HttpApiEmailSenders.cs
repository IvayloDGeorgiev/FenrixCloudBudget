using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;

namespace FenrixCloudBudget.Services.Email.Adapters;

/// <summary>Shared helper for the simple API-key-over-HTTPS providers.</summary>
internal static class HttpEmailHelpers
{
    public static EmailResult Interpret(HttpResponseMessage resp, string body)
        => resp.IsSuccessStatusCode
            ? EmailResult.Ok(body.Length > 0 ? body : null)
            : EmailResult.Fail($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");
}

/// <summary>Resend adapter. Fastest setup; recommended starter. Field: API key + From address.</summary>
public sealed class ResendEmailSender : IEmailSender
{
    private readonly IHttpClientFactory _http;
    private readonly EmailAdapterContext _ctx;
    public ResendEmailSender(IHttpClientFactory http, EmailAdapterContext ctx) { _http = http; _ctx = ctx; }

    public EmailMethod Method => EmailMethod.Resend;
    public IReadOnlyList<EmailFieldSpec> FieldSchema => new[]
    {
        new EmailFieldSpec("apiKey", "API key", IsSecret: true, Placeholder: "re_..."),
        new EmailFieldSpec("fromAddress", "From address (verified domain)")
    };

    public async Task<EmailResult> SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        try
        {
            var o = await _ctx.GetOptionsAsync(ct);
            var client = _http.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", await _ctx.GetSecretAsync("apiKey", ct));
            var resp = await client.PostAsJsonAsync("https://api.resend.com/emails", new
            {
                from = o["fromAddress"],
                to = new[] { m.To },
                subject = m.Subject,
                html = m.HtmlBody
            }, ct);
            return HttpEmailHelpers.Interpret(resp, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) { return EmailResult.Fail(ex.Message); }
    }

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default)
        => SendAsync(new EmailMessage(to, "FenrixCloudBudget test email", "<p>Resend works. 🎉</p>"), ct);
}

/// <summary>SendGrid adapter. Field: API key + From address.</summary>
public sealed class SendGridEmailSender : IEmailSender
{
    private readonly IHttpClientFactory _http;
    private readonly EmailAdapterContext _ctx;
    public SendGridEmailSender(IHttpClientFactory http, EmailAdapterContext ctx) { _http = http; _ctx = ctx; }

    public EmailMethod Method => EmailMethod.SendGrid;
    public IReadOnlyList<EmailFieldSpec> FieldSchema => new[]
    {
        new EmailFieldSpec("apiKey", "API key", IsSecret: true, Placeholder: "SG..."),
        new EmailFieldSpec("fromAddress", "From address")
    };

    public async Task<EmailResult> SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        try
        {
            var o = await _ctx.GetOptionsAsync(ct);
            var client = _http.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", await _ctx.GetSecretAsync("apiKey", ct));
            var payload = new
            {
                personalizations = new[] { new { to = new[] { new { email = m.To } } } },
                from = new { email = o["fromAddress"] },
                subject = m.Subject,
                content = new[] { new { type = "text/html", value = m.HtmlBody } }
            };
            var resp = await client.PostAsJsonAsync("https://api.sendgrid.com/v3/mail/send", payload, ct);
            return HttpEmailHelpers.Interpret(resp, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) { return EmailResult.Fail(ex.Message); }
    }

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default)
        => SendAsync(new EmailMessage(to, "FenrixCloudBudget test email", "<p>SendGrid works. 🎉</p>"), ct);
}

/// <summary>Postmark adapter. Best deliverability for transactional/OTP. Field: server token + From address.</summary>
public sealed class PostmarkEmailSender : IEmailSender
{
    private readonly IHttpClientFactory _http;
    private readonly EmailAdapterContext _ctx;
    public PostmarkEmailSender(IHttpClientFactory http, EmailAdapterContext ctx) { _http = http; _ctx = ctx; }

    public EmailMethod Method => EmailMethod.Postmark;
    public IReadOnlyList<EmailFieldSpec> FieldSchema => new[]
    {
        new EmailFieldSpec("serverToken", "Server token", IsSecret: true),
        new EmailFieldSpec("fromAddress", "From address")
    };

    public async Task<EmailResult> SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        try
        {
            var o = await _ctx.GetOptionsAsync(ct);
            var client = _http.CreateClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("X-Postmark-Server-Token", await _ctx.GetSecretAsync("serverToken", ct));
            var resp = await client.PostAsJsonAsync("https://api.postmarkapp.com/email", new
            {
                From = o["fromAddress"],
                To = m.To,
                Subject = m.Subject,
                HtmlBody = m.HtmlBody,
                MessageStream = "outbound"
            }, ct);
            return HttpEmailHelpers.Interpret(resp, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) { return EmailResult.Fail(ex.Message); }
    }

    public Task<EmailResult> SendTestAsync(string to, CancellationToken ct = default)
        => SendAsync(new EmailMessage(to, "FenrixCloudBudget test email", "<p>Postmark works. 🎉</p>"), ct);
}
