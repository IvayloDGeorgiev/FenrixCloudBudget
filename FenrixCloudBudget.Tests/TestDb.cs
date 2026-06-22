using FenrixCloudBudget.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Tests;

/// <summary>Creates an isolated in-memory SQLite context + a matching IDbContextFactory for tests.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public IDbContextFactory<AppDbContext> Factory { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var db = new AppDbContext(options))
            db.Database.EnsureCreated();

        Factory = new PooledFactory(options);
    }

    public AppDbContext NewContext() => Factory.CreateDbContext();

    public void Dispose() => _connection.Dispose();

    private sealed class PooledFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public PooledFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
    }
}
