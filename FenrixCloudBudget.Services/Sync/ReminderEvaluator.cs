using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Sync;

/// <summary>
/// Fires reminders (client secret / certificate / custom) at each configured lead time
/// (e.g. 30/14/7/1 days before due) and on the due date. Respects snooze and only fires
/// Active reminders. De-duplicates per lead-time per day via the notification service.
/// </summary>
public sealed class ReminderEvaluator
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly INotificationService _notifications;

    public ReminderEvaluator(IDbContextFactory<AppDbContext> dbf, INotificationService notifications)
    {
        _dbf = dbf;
        _notifications = notifications;
    }

    public async Task EvaluateAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var reminders = await db.Reminders
            .Where(r => r.Status == ReminderStatus.Active)
            .ToListAsync(ct);

        foreach (var r in reminders)
        {
            if (r.SnoozedUntilUtc is { } snooze && snooze > now) continue;

            var dueDate = DateOnly.FromDateTime(r.DueDate.UtcDateTime);
            var daysUntil = dueDate.DayNumber - today.DayNumber;

            // Fire when today matches a configured lead time, or it's due/overdue.
            var matchesLead = r.LeadTimes().Contains(daysUntil);
            var dueOrOverdue = daysUntil <= 0;
            if (!matchesLead && !dueOrOverdue) continue;

            var kind = r.Type switch
            {
                ReminderType.ClientSecret => "Client secret",
                ReminderType.Certificate => "Certificate",
                _ => "Reminder"
            };

            var template = r.Type == ReminderType.Custom
                ? Email.Templates.EmailTemplateRenderer.ReminderDue
                : Email.Templates.EmailTemplateRenderer.SecretExpiring;

            var dedupe = $"reminder:{r.Id}:lead{daysUntil}:{today:yyyy-MM-dd}";

            await _notifications.DispatchAsync(new NotificationRequest(
                NotificationSourceType.Reminder,
                r.Id,
                r.Channels | NotificationChannel.InApp,
                Title: dueOrOverdue ? $"{kind} due: {r.Title}" : $"{kind} due in {daysUntil} day(s): {r.Title}",
                Message: $"{r.Title} is due on {dueDate:yyyy-MM-dd}. {r.Notes}",
                EmailTo: null,
                EmailTemplateKey: template,
                EmailData: new { r.Title, Kind = kind, DueDate = dueDate.ToString("yyyy-MM-dd"), r.Notes },
                DedupeKey: dedupe), ct);

            r.LastFiredUtc = now;

            // Roll a repeating reminder forward once it has fired on/after the due date.
            if (dueOrOverdue && r.RecurrenceDays > 0)
                r.DueDate = r.DueDate.AddDays(r.RecurrenceDays);
        }

        await db.SaveChangesAsync(ct);
    }
}
