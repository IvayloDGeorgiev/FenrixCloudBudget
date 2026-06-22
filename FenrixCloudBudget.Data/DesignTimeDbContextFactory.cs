using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FenrixCloudBudget.Data;

/// <summary>
/// Used by `dotnet ef migrations add ...` at design time. Always targets SQLite so
/// the migration model can be generated without a live SQL Server. The SQLite and
/// SQL Server schemas are compatible for this model.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=fenrix.design.db")
            .Options;
        return new AppDbContext(options);
    }
}
