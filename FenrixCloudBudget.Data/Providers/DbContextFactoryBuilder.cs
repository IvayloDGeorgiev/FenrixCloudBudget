using FenrixCloudBudget.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data.Providers;

/// <summary>Centralizes how DbContextOptions are built for each backend mode.</summary>
public static class DbContextFactoryBuilder
{
    public static DbContextOptions<AppDbContext> Build(DataProviderOptions opts)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        Configure(builder, opts);
        return builder.Options;
    }

    public static void Configure(DbContextOptionsBuilder builder, DataProviderOptions opts)
    {
        switch (opts.Mode)
        {
            case DataProviderMode.SqlServer:
            case DataProviderMode.CloudSql:
                builder.UseSqlServer(opts.ConnectionString
                    ?? throw new InvalidOperationException("ConnectionString required for SQL Server / Cloud SQL mode."));
                break;

            case DataProviderMode.Saas:
                // SaaS keeps a local SQLite cache so the app opens offline with last-known data.
                builder.UseSqlite($"Data Source={opts.SqlitePath ?? "fenrix.cache.db"}");
                break;

            case DataProviderMode.Sqlite:
            default:
                builder.UseSqlite($"Data Source={opts.SqlitePath ?? "fenrix.db"}");
                break;
        }
    }
}
