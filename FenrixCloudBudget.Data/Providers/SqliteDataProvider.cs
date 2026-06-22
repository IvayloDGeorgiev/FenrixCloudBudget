using System.Diagnostics;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data.Providers;

/// <summary>Default on-device backend. Applies migrations to a local SQLite file.</summary>
public sealed class SqliteDataProvider : IDataProvider
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public DataProviderMode Mode => DataProviderMode.Sqlite;

    public SqliteDataProvider(IDbContextFactory<AppDbContext> factory) => _factory = factory;

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
            return new DataProviderTestResult(ok, ok ? "Connected" : "Cannot open database", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DataProviderTestResult(false, ex.Message);
        }
    }
}
