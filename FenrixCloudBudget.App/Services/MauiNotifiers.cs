using FenrixCloudBudget.Services.Notifications;
using Plugin.LocalNotification;

namespace FenrixCloudBudget.App.Services;

/// <summary>
/// In-app channel. Raises an event the Blazor layout subscribes to (toast + Notifications
/// center badge). Also keeps a small in-memory feed for the Notifications panel.
/// </summary>
public sealed class MauiInAppNotifier : IInAppNotifier
{
    public event Action? Changed;
    private readonly List<InAppNotice> _feed = new();
    public IReadOnlyList<InAppNotice> Feed => _feed;
    public int UnreadCount { get; private set; }

    public Task ShowAsync(string title, string message, CancellationToken ct = default)
    {
        _feed.Insert(0, new InAppNotice(title, message, DateTimeOffset.Now));
        if (_feed.Count > 100) _feed.RemoveAt(_feed.Count - 1);
        UnreadCount++;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public void MarkAllRead()
    {
        UnreadCount = 0;
        Changed?.Invoke();
    }
}

public record InAppNotice(string Title, string Message, DateTimeOffset At);

/// <summary>Local device notification channel via Plugin.LocalNotification.</summary>
public sealed class MauiLocalNotifier : ILocalNotifier
{
    public async Task ScheduleAsync(string title, string message, DateTimeOffset? at = null, CancellationToken ct = default)
    {
        var request = new NotificationRequest
        {
            NotificationId = Random.Shared.Next(100_000, 999_999),
            Title = title,
            Description = message,
            Schedule = at is { } when
                ? new NotificationRequestSchedule { NotifyTime = when.LocalDateTime }
                : null
        };
        await LocalNotificationCenter.Current.Show(request);
    }
}
