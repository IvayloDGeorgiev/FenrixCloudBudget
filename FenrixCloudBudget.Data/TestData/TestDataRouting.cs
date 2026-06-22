using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Data.Providers;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data.TestData;

/// <summary>
/// Runtime flag for "test data mode". When enabled, the whole app reads/writes an isolated
/// test database instead of the real backend — so demo/test data never mixes with real data.
/// The host (MAUI) persists the value (e.g. in Preferences) and restores it on startup.
/// </summary>
public sealed class TestDataState
{
    private bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        private set
        {
            if (_enabled == value) return;
            _enabled = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Raised whenever the toggle changes so the host can persist it and refresh the UI.</summary>
    public event Action? Changed;

    public void Set(bool enabled) => Enabled = enabled;

    /// <summary>Set the initial value at startup without raising Changed (avoids a persist echo).</summary>
    public void Initialize(bool enabled) => _enabled = enabled;
}

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> that returns a context pointing at either the real
/// backend or an isolated test SQLite database, chosen by <see cref="TestDataState"/> at call time.
/// Because every page and service resolves the context through this factory, flipping the toggle
/// instantly switches what data the entire app sees — even when the real backend is SQL Server or
/// a remote DB. The test database is created and seeded on demand.
/// </summary>
public sealed class RoutingDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly TestDataState _state;
    private readonly DbContextOptions<AppDbContext> _realOptions;
    private readonly DbContextOptions<AppDbContext> _testOptions;
    private readonly SemaphoreSlim _seedGate = new(1, 1);
    private bool _testReady;

    public RoutingDbContextFactory(DataProviderOptions realOptions, TestDataState state)
    {
        _state = state;
        _realOptions = DbContextFactoryBuilder.Build(realOptions);

        // Place the test DB next to the real SQLite file (or in the working dir for server backends).
        var dir = !string.IsNullOrWhiteSpace(realOptions.SqlitePath)
            ? Path.GetDirectoryName(realOptions.SqlitePath)
            : null;
        var testPath = Path.Combine(string.IsNullOrWhiteSpace(dir) ? "." : dir!, "fenrix.test.db");
        _testOptions = DbContextFactoryBuilder.Build(new DataProviderOptions
        {
            Mode = DataProviderMode.Sqlite,
            SqlitePath = testPath
        });
    }

    public AppDbContext CreateDbContext()
        => new(_state.Enabled ? _testOptions : _realOptions);

    /// <summary>Create &amp; seed the isolated test database if it hasn't been prepared yet.</summary>
    public async Task EnsureTestReadyAsync(CancellationToken ct = default)
    {
        if (_testReady) return;
        await _seedGate.WaitAsync(ct);
        try
        {
            if (_testReady) return;
            await using var db = new AppDbContext(_testOptions);
            await db.Database.EnsureCreatedAsync(ct);
            await TestDataSeeder.SeedAsync(db, ct);
            _testReady = true;
        }
        finally
        {
            _seedGate.Release();
        }
    }
}
