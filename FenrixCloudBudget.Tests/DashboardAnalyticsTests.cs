using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Services.Analytics;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class DashboardAnalyticsTests
{
    [Fact]
    public async Task Computes_Pacing_Projection_And_Breakdowns()
    {
        using var test = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        await using (var db = test.NewContext())
        {
            var project = new Project { Name = "P", Currency = "USD" };
            var svc = new Service { Name = "vm", Provider = CloudProvider.Aws, Source = ServiceSource.Connected, Currency = "USD" };
            // £10/day from month start to today
            for (var d = monthStart; d <= today; d = d.AddDays(1))
                svc.CostRecords.Add(new CostRecord { Date = d, Amount = 10m, Currency = "USD", Source = ServiceSource.Connected });
            project.Services.Add(svc);
            project.Budgets.Add(new Budget { Name = "cap", Period = BudgetPeriod.Monthly, Amount = 1000m, Currency = "USD", Thresholds = "50,80,100", IsEnabled = true });
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        }

        var analytics = new DashboardAnalytics(test.Factory);
        var data = await analytics.GetAsync(new DashboardQuery("All", null, monthStart, today));

        Assert.Equal(1000m, data.MonthlyBudget);
        Assert.Equal(10m * today.Day, data.MonthToDateSpend);
        // run-rate projection ≈ £10/day × days in month
        Assert.InRange((double)data.ProjectedMonthEnd, 10.0 * daysInMonth - 0.01, 10.0 * daysInMonth + 0.01);
        Assert.Equal(daysInMonth, data.Pacing.Count);
        Assert.True(data.ByProvider.ContainsKey("Aws"));
        Assert.Contains(data.Budgets, b => b.Project == "P");
    }

    [Fact]
    public async Task TopMovers_DetectsIncreaseVsPreviousPeriod()
    {
        using var test = new TestDb();
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-6);          // 7-day window
        var prevFrom = from.AddDays(-7);

        await using (var db = test.NewContext())
        {
            var project = new Project { Name = "P", Currency = "USD" };
            var svc = new Service { Name = "spiky", Provider = CloudProvider.Aws, Source = ServiceSource.Connected, Currency = "USD" };
            // previous window: £1/day; current window: £20/day → big positive mover
            for (var d = prevFrom; d < from; d = d.AddDays(1))
                svc.CostRecords.Add(new CostRecord { Date = d, Amount = 1m, Currency = "USD", Source = ServiceSource.Connected });
            for (var d = from; d <= to; d = d.AddDays(1))
                svc.CostRecords.Add(new CostRecord { Date = d, Amount = 20m, Currency = "USD", Source = ServiceSource.Connected });
            project.Services.Add(svc);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        }

        var analytics = new DashboardAnalytics(test.Factory);
        var data = await analytics.GetAsync(new DashboardQuery("All", null, from, to));

        var mover = Assert.Single(data.TopMovers);
        Assert.Equal("spiky", mover.Label);
        Assert.True(mover.Delta > 0);
    }
}
