using System.Text.Json;

namespace FenrixCloudBudget.Services.Email.Templates;

/// <summary>
/// Renders branded HTML email bodies from a template key + data object. Templates are
/// shared across all providers (budget breach, reminder due, secret/cert expiring, OTP,
/// invitation). Kept as simple string interpolation so there's no extra dependency; swap
/// for Razor/Scriban later if richer templating is needed.
/// </summary>
public interface IEmailTemplateRenderer
{
    (string Subject, string Html, string Text) Render(string templateKey, object data);
}

public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public const string BudgetBreach = "budget-breach";
    public const string ReminderDue = "reminder-due";
    public const string SecretExpiring = "secret-expiring";
    public const string Otp = "otp";
    public const string Invitation = "invitation";

    public (string Subject, string Html, string Text) Render(string templateKey, object data)
    {
        var d = ToDict(data);
        return templateKey switch
        {
            BudgetBreach => (
                $"Budget alert: {d.GetValueOrDefault("ProjectName")} at {d.GetValueOrDefault("Percent")}%",
                Shell($"Budget threshold reached",
                    $"<p><strong>{d.GetValueOrDefault("ProjectName")}</strong> has reached " +
                    $"<strong>{d.GetValueOrDefault("Percent")}%</strong> of its " +
                    $"{d.GetValueOrDefault("Period")} budget " +
                    $"({d.GetValueOrDefault("Spend")} of {d.GetValueOrDefault("Amount")} {d.GetValueOrDefault("Currency")}).</p>"),
                $"{d.GetValueOrDefault("ProjectName")} reached {d.GetValueOrDefault("Percent")}% of budget."),

            ReminderDue => (
                $"Reminder: {d.GetValueOrDefault("Title")} due {d.GetValueOrDefault("DueDate")}",
                Shell("Reminder due",
                    $"<p><strong>{d.GetValueOrDefault("Title")}</strong> is due on " +
                    $"<strong>{d.GetValueOrDefault("DueDate")}</strong>.</p>" +
                    $"<p>{d.GetValueOrDefault("Notes")}</p>"),
                $"{d.GetValueOrDefault("Title")} due {d.GetValueOrDefault("DueDate")}."),

            SecretExpiring => (
                $"{d.GetValueOrDefault("Kind")} expiring: {d.GetValueOrDefault("Title")}",
                Shell($"{d.GetValueOrDefault("Kind")} expiring soon",
                    $"<p>The {d.GetValueOrDefault("Kind")} for <strong>{d.GetValueOrDefault("Title")}</strong> " +
                    $"expires on <strong>{d.GetValueOrDefault("DueDate")}</strong>. Rotate it before then to avoid an outage.</p>"),
                $"{d.GetValueOrDefault("Kind")} for {d.GetValueOrDefault("Title")} expires {d.GetValueOrDefault("DueDate")}."),

            Otp => (
                "Your FenrixCloudBudget sign-in code",
                Shell("Your sign-in code",
                    $"<p style='font-size:28px;letter-spacing:6px;font-weight:700'>{d.GetValueOrDefault("Code")}</p>" +
                    $"<p>This code expires in {d.GetValueOrDefault("Minutes")} minutes. If you didn't request it, ignore this email.</p>"),
                $"Your code is {d.GetValueOrDefault("Code")} (expires in {d.GetValueOrDefault("Minutes")} minutes)."),

            Invitation => (
                $"You've been invited to {d.GetValueOrDefault("OrgName")} on FenrixCloudBudget",
                Shell("You're invited",
                    $"<p>You've been invited to join <strong>{d.GetValueOrDefault("OrgName")}</strong>.</p>" +
                    $"<p>Your invite code: <strong style='letter-spacing:4px'>{d.GetValueOrDefault("Code")}</strong></p>"),
                $"Invite code: {d.GetValueOrDefault("Code")}"),

            _ => ("FenrixCloudBudget notification", Shell("Notification", $"<p>{JsonSerializer.Serialize(data)}</p>"), "Notification")
        };
    }

    private static Dictionary<string, string> ToDict(object data)
    {
        if (data is IDictionary<string, object?> dyn)
            return dyn.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? string.Empty);

        return data.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(data)?.ToString() ?? string.Empty);
    }

    /// <summary>Branded outer HTML wrapper (FenrixCloudBudget — "Group. Budget. Stay ahead.").</summary>
    private static string Shell(string heading, string bodyHtml) => $@"
<!doctype html><html><body style='margin:0;background:#0B1020;font-family:Segoe UI,Roboto,Arial,sans-serif'>
  <div style='max-width:560px;margin:0 auto;padding:32px'>
    <div style='background:#ffffff;border-radius:16px;padding:28px'>
      <div style='font-weight:800;font-size:18px;color:#0B1020'>FenrixCloudBudget</div>
      <div style='color:#6b7280;font-size:12px;margin-bottom:18px'>Group. Budget. Stay ahead.</div>
      <h2 style='color:#111827;font-size:20px;margin:0 0 12px'>{heading}</h2>
      <div style='color:#374151;font-size:14px;line-height:1.6'>{bodyHtml}</div>
    </div>
    <div style='color:#6b7280;font-size:11px;text-align:center;margin-top:16px'>
      Sent by FenrixCloudBudget · You can adjust alerts in Settings → Email &amp; Notifications.
    </div>
  </div>
</body></html>";
}
