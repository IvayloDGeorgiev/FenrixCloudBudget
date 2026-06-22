using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Services.Sync;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class BudgetEvaluatorTests
{
    [Fact]
    public async Task FiresAlert_WhenManualEstimatesCrossThreshold()
    {
        using var test = new TestDb();
        await using (var db = test.NewContext())
        {
            var project = new Project { Name = "P1", Currency = "USD" };
            project.Services.Add(new Service { Name = "svc", Provider = CloudProvider.Aws, EstimatedCost = 90m, Currency = "USD" });
            project.Budgets.Add(new Budget { Name = "cap", Amount = 100m, Currency = "USD", Thresholds = "50,80,100" });
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        }

        var spy = new SpyNotificationService();
        var evaluator = new BudgetEvaluator(test.Factory, spy);

        await evaluator.EvaluateAllAsync();

        // 90 of 100 = 90% -> highest crossed threshold is 80%.
        Assert.Single(spy.Dispatched);
        Assert.Contains("80%", spy.Dispatched[0].Title);
        Assert.Equal(NotificationSourceType.Budget, spy.Dispatched[0].SourceType);
    }

    [Fact]
    public async Task NoAlert_WhenUnderAllThresholds()
    {
        using var test = new TestDb();
        await using (var db = test.NewContext())
        {
            var project = new Project { Name = "P2", Currency = "USD" };
            project.Services.Add(new Service { Name = "svc", Provider = CloudProvider.Azure, EstimatedCost = 10m, Currency = "USD" });
            project.Budgets.Add(new Budget { Name = "cap", Amount = 100m, Currency = "USD", Thresholds = "50,80,100" });
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        }

        var spy = new SpyNotificationService();
        await new BudgetEvaluator(test.Factory, spy).EvaluateAllAsync();

        Assert.Empty(spy.Dispatched);
    }

    [Fact]
    public async Task UsesActualPerConnectedService_AndEstimateForManualService()
    {
        using var test = new TestDb();
        await using (var db = test.NewContext())
        {
            var account = new CloudAccount
            {
                DisplayName = "AWS",
                Provider = CloudProvider.Aws,
                LastSyncedUtc = DateTimeOffset.UtcNow
            };
            var project = new Project { Name = "Mixed", Currency = "USD" };
            var connected = new Service
            {
                Name = "EC2",
                Provider = CloudProvider.Aws,
                Source = ServiceSource.Connected,
                CloudAccount = account,
                EstimatedCost = 90m,
                Currency = "USD"
            };
            project.Services.Add(connected);
            project.Services.Add(new Service
            {
                Name = "Manual support",
                Provider = CloudProvider.Aws,
                Source = ServiceSource.Manual,
                EstimatedCost = 30m,
                Currency = "USD"
            });
            project.Budgets.Add(new Budget
            {
                Name = "cap",
                Amount = 100m,
                Currency = "USD",
                Thresholds = "50,80,100"
            });
            db.Projects.Add(project);
            db.CostRecords.Add(new CostRecord
            {
                Service = connected,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = 20m,
                Currency = "USD"
            });
            await db.SaveChangesAsync();
        }

        var spy = new SpyNotificationService();
        await new BudgetEvaluator(test.Factory, spy).EvaluateAllAsync();

        Assert.Single(spy.Dispatched);
        Assert.Contains("50%", spy.Dispatched[0].Title); // 20 actual + 30 manual estimate
    }

    private sealed class SpyNotificationService : INotificationService
    {
        public List<NotificationRequest> Dispatched { get; } = new();
        public Task DispatchAsync(NotificationRequest request, CancellationToken ct = default)
        {
            Dispatched.Add(request);
            return Task.CompletedTask;
        }
        public Task NotifyInAppAsync(string title, string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyLocalAsync(string title, string message, DateTimeOffset? at = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> SendEmailAsync(string to, string templateKey, object data, CancellationToken ct = default) => Task.FromResult(true);
    }
}
