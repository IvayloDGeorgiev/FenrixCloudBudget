using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Interfaces;

/// <summary>
/// Pluggable storage backend selected in Settings -> Data &amp; Connections.
/// SQLite is the default; SQL Server / Cloud SQL / SaaS are runtime-selectable.
/// Implementations live in FenrixCloudBudget.Data.Providers and ultimately
/// configure the EF Core DbContext for the chosen mode.
/// </summary>
public interface IDataProvider
{
    DataProviderMode Mode { get; }

    /// <summary>Ensure the store exists and migrations are applied.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Validate connectivity for the "Test connection" button before saving.</summary>
    Task<DataProviderTestResult> TestConnectionAsync(CancellationToken ct = default);
}

public record DataProviderTestResult(bool Success, string? Message = null, TimeSpan? Latency = null);
