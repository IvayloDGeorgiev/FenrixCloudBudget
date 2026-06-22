using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Email;
using FenrixCloudBudget.Services.Email.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Services.Notifications;

/// <summary>
/// The single outbound messaging layer for budget alerts, reminders, and OTP/invitations.
/// Fans out to in-app / local device / email channels, honours quiet hours, and records every
/// send in NotificationDelivery for de-duplication and audit.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly IInAppNotifier _inApp;
    private readonly ILocalNotifier _local;
    private readonly IEmailSenderFactory _emailFactory;
    private readonly IEmailTemplateRenderer _templates;
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(
        IInAppNotifier inApp,
        ILocalNotifier local,
        IEmailSenderFactory emailFactory,
        IEmailTemplateRenderer templates,
        IDbContextFactory<AppDbContext> dbf,
        ILogger<NotificationService> log)
    {
        _inApp = inApp;
        _local = local;
        _emailFactory = emailFactory;
        _templates = templates;
        _dbf = dbf;
        _log = log;
    }

    public Task NotifyInAppAsync(string title, string message, CancellationToken ct = default)
        => _inApp.ShowAsync(title, message, ct);

    public Task NotifyLocalAsync(string title, string message, DateTimeOffset? at = null, CancellationToken ct = default)
        => _local.ScheduleAsync(title, message, at, ct);

    public async Task<bool> SendEmailAsync(string to, string templateKey, object data, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var cfg = await db.EmailConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.Enabled || cfg.Method == EmailMethod.InAppAndDeviceOnly)
            return false;

        var (subject, html, text) = _templates.Render(templateKey, data);
        var sender = _emailFactory.Resolve(cfg.Method);
        var result = await sender.SendAsync(new EmailMessage(to, subject, html, text), ct);

        if (!result.Success)
            _log.LogWarning("Email send failed via {Method}: {Error}", cfg.Method, result.Error);

        return result.Success;
    }

    public async Task DispatchAsync(NotificationRequest request, CancellationToken ct = default)
    {
        // De-duplication: skip if this exact key was already delivered.
        if (!string.IsNullOrEmpty(request.DedupeKey) && await AlreadyDeliveredAsync(request.DedupeKey, ct))
            return;

        var quiet = await IsQuietHoursAsync(ct);

        if (request.Channels.HasFlag(NotificationChannel.InApp))
            await RecordAsync(request, NotificationChannel.InApp, async () =>
            {
                await _inApp.ShowAsync(request.Title, request.Message, ct);
                return DeliveryResult.Sent;
            }, ct);

        if (request.Channels.HasFlag(NotificationChannel.LocalDevice))
            await RecordAsync(request, NotificationChannel.LocalDevice, async () =>
            {
                if (quiet) return DeliveryResult.Suppressed;
                await _local.ScheduleAsync(request.Title, request.Message, null, ct);
                return DeliveryResult.Sent;
            }, ct);

        if (request.Channels.HasFlag(NotificationChannel.Email) && request.EmailTo is not null && request.EmailTemplateKey is not null)
            await RecordAsync(request, NotificationChannel.Email, async () =>
            {
                var ok = await SendEmailAsync(request.EmailTo, request.EmailTemplateKey, request.EmailData ?? new { }, ct);
                return ok ? DeliveryResult.Sent : DeliveryResult.Failed;
            }, ct);
    }

    private async Task<bool> AlreadyDeliveredAsync(string dedupeKey, CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.NotificationDeliveries
            .AnyAsync(x => x.DedupeKey == dedupeKey && x.Result == DeliveryResult.Sent, ct);
    }

    private async Task<bool> IsQuietHoursAsync(CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var cfg = await db.EmailConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || cfg.QuietHoursFrom == cfg.QuietHoursTo) return false;

        var now = TimeOnly.FromDateTime(DateTime.Now);
        return cfg.QuietHoursFrom < cfg.QuietHoursTo
            ? now >= cfg.QuietHoursFrom && now < cfg.QuietHoursTo
            : now >= cfg.QuietHoursFrom || now < cfg.QuietHoursTo; // window crosses midnight
    }

    private async Task RecordAsync(NotificationRequest req, NotificationChannel channel, Func<Task<DeliveryResult>> act, CancellationToken ct)
    {
        var result = DeliveryResult.Failed;
        string? error = null;
        try
        {
            result = await act();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _log.LogWarning(ex, "Notification channel {Channel} failed", channel);
        }

        await using var db = await _dbf.CreateDbContextAsync(ct);
        db.NotificationDeliveries.Add(new Core.Entities.NotificationDelivery
        {
            SourceType = req.SourceType,
            SourceId = req.SourceId,
            Channel = channel,
            Recipient = channel == NotificationChannel.Email ? req.EmailTo : null,
            Subject = req.Title,
            Result = result,
            Error = error,
            DedupeKey = req.DedupeKey
        });
        await db.SaveChangesAsync(ct);
    }
}
