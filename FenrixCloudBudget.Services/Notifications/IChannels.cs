namespace FenrixCloudBudget.Services.Notifications;

/// <summary>
/// In-app channel (toast/badge + Notifications center). The MAUI/Blazor host implements
/// this with a UI-bound implementation; a no-op default lets the API/tests run headless.
/// </summary>
public interface IInAppNotifier
{
    Task ShowAsync(string title, string message, CancellationToken ct = default);
}

/// <summary>
/// Local device notification channel (Plugin.LocalNotification on Android/Windows).
/// The MAUI host implements this; headless hosts use the no-op default.
/// </summary>
public interface ILocalNotifier
{
    Task ScheduleAsync(string title, string message, DateTimeOffset? at = null, CancellationToken ct = default);
}

/// <summary>No-op fallbacks so non-UI hosts (API, tests) resolve the dependencies cleanly.</summary>
public sealed class NullInAppNotifier : IInAppNotifier
{
    public Task ShowAsync(string title, string message, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class NullLocalNotifier : ILocalNotifier
{
    public Task ScheduleAsync(string title, string message, DateTimeOffset? at = null, CancellationToken ct = default) => Task.CompletedTask;
}
