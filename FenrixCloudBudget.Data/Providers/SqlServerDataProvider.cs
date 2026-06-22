using System.Diagnostics;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data.Providers;

/// <summary>
/// User-supplied SQL Server backend. Also serves Cloud SQL (Azure SQL) via a
/// connection string — only the reported Mode differs.
/// </summary>
public sealed class SqlServerDataProvider : IDataProvider
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public DataProviderMode Mode { get; }

    public SqlServerDataProvider(IDbContextFactory<AppDbContext> factory, DataProviderMode mode = DataProviderMode.SqlServer)
    {
        _factory = factory;
        Mode = mode;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        await DbSeeder.SeedAsync(db, ct);
    }

    public async Task<DataProviderTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var ok = await db.Database.CanConnectAsync(ct);
            sw.Stop();
            return new DataProviderTestResult(ok, ok ? "Connected" : "Cannot reach server", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DataProviderTestResult(false, ex.Message);
        }
    }
}
