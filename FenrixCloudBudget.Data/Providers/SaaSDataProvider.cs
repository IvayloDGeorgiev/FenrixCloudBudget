using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data.Providers;

/// <summary>
/// SaaS mode: the hosted API is the source of truth; a local SQLite file acts as a
/// read cache so the app still opens offline. Sync logic lands in Phase 5.
/// </summary>
public sealed class SaaSDataProvider : IDataProvider
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly string _baseUrl;
    public DataProviderMode Mode => DataProviderMode.Saas;

    public SaaSDataProvider(IDbContextFactory<AppDbContext> factory, string baseUrl)
    {
        _factory = factory;
        _baseUrl = baseUrl;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Local cache schema is the same; the API hydrates it on sync.
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        // TODO(Phase 5): pull latest snapshot from {_baseUrl} into the local cache.
    }

    public Task<DataProviderTestResult> TestConnectionAsync(CancellationToken ct = default)
        // TODO(Phase 5): ping {_baseUrl}/health with the session token.
        => Task.FromResult(new DataProviderTestResult(false, "SaaS mode lands in Phase 5."));
}
