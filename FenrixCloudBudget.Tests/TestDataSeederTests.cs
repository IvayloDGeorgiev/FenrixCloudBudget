using FenrixCloudBudget.Data.TestData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class TestDataSeederTests
{
    [Fact]
    public async Task Seed_Creates5ClientsWithProjectsServicesAndBudgets()
    {
        using var test = new TestDb();

        await using (var db = test.NewContext())
            await TestDataSeeder.SeedAsync(db);

        await using var verify = test.NewContext();
        Assert.Equal(5, await verify.Clients.CountAsync());
        Assert.True(await verify.Projects.CountAsync() >= 10);   // 5 clients x 2-3 projects
        Assert.True(await verify.Services.CountAsync() > 0);
        Assert.True(await verify.Budgets.CountAsync() >= 10);    // one per project
        Assert.True(await verify.CostRecords.CountAsync() > 0);  // connected services have history
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        using var test = new TestDb();

        await using (var db = test.NewContext())
            await TestDataSeeder.SeedAsync(db);
        await using (var db = test.NewContext())
            await TestDataSeeder.SeedAsync(db);   // second run must not duplicate

        await using var verify = test.NewContext();
        Assert.Equal(5, await verify.Clients.CountAsync());
    }
}
