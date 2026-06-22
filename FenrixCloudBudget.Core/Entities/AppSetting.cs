using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>Singleton-ish app configuration row (theme, data mode, sync interval, auth mode).</summary>
public class AppSetting : EntityBase
{
    public string ThemeId { get; set; } = "daybreak";
    public bool FollowSystemDarkMode { get; set; } = true;

    public DataProviderMode DataMode { get; set; } = DataProviderMode.Sqlite;
    public string? ConnectionStringReference { get; set; }   // secure-store ref for server modes

    public AuthMode AuthMode { get; set; } = AuthMode.LocalNone;

    /// <summary>Auto-sync interval in hours (respects AWS per-request cost / rate limits).</summary>
    public int SyncIntervalHours { get; set; } = 12;

    public string DefaultCurrency { get; set; } = "USD";
}
