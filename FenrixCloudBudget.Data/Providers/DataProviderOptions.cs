using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Data.Providers;

/// <summary>Runtime configuration for the active data backend (built from AppSetting + secure store).</summary>
public class DataProviderOptions
{
    public DataProviderMode Mode { get; set; } = DataProviderMode.Sqlite;

    /// <summary>SQLite file path (default mode), e.g. {AppData}/fenrix.db.</summary>
    public string? SqlitePath { get; set; }

    /// <summary>Connection string for SQL Server / Cloud SQL modes (resolved from the secure store).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Base address of the hosted API for SaaS mode.</summary>
    public string? SaasBaseUrl { get; set; }

    /// <summary>
    /// Current bearer session for SaaS mode. Hosts should resolve this from secure storage
    /// when constructing the provider; it is intentionally never written to the database.
    /// </summary>
    public string? SaasAccessToken { get; set; }
}
