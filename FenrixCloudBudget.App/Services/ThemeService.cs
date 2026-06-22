namespace FenrixCloudBudget.App.Services;

/// <summary>
/// Holds the active theme id and notifies the UI when it changes. Themes are pure CSS-variable
/// token sets (see wwwroot/themes) so switching is instant and adding a theme needs no code.
/// </summary>
public sealed class ThemeService
{
    public record ThemeInfo(string Id, string Name, bool Dark);

    public static readonly IReadOnlyList<ThemeInfo> Available = new[]
    {
        new ThemeInfo("aurora", "Aurora (light)", false),
        new ThemeInfo("midnight", "Midnight (dark)", true),
        new ThemeInfo("slate", "Slate (neutral)", false),
        new ThemeInfo("mint", "Mint (accent)", false)
    };

    public string CurrentThemeId { get; private set; } = "aurora";
    public event Action? Changed;

    public void SetTheme(string themeId)
    {
        if (CurrentThemeId == themeId) return;
        CurrentThemeId = themeId;
        Changed?.Invoke();
    }
}

/// <summary>Lightweight per-circuit UI state (global dashboard filters, etc.).</summary>
public sealed class AppState
{
    public string ProviderFilter { get; set; } = "All";
    public int? ClientFilter { get; set; }
    public int? ProjectFilter { get; set; }
    public DateOnly RangeFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly RangeTo { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public event Action? FiltersChanged;
    public void NotifyFiltersChanged() => FiltersChanged?.Invoke();
}
