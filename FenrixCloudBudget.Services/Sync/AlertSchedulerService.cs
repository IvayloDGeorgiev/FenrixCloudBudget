using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Services.Sync;

/// <summary>
/// Background loop that evaluates budgets and reminders on startup and then on an interval.
/// Phase 1: runs the budget + reminder evaluators (in-app/local alerts work with no cloud).
/// Phase 4: a cost-sync pass is added before evaluation (rate-limit aware, AWS per-request cost aware).
/// </summary>
public sealed class AlertSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AlertSchedulerService> _log;
    private readonly TimeSpan _interval;

    public AlertSchedulerService(IServiceScopeFactory scopes, ILogger<AlertSchedulerService> log, TimeSpan? interval = null)
    {
        _scopes = scopes;
        _log = log;
        _interval = interval ?? TimeSpan.FromHours(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First pass shortly after launch so the user sees current state immediately.
        await RunOnceSafe(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceSafe(stoppingToken);
    }

    public async Task RunOnceSafe(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var costSync = scope.ServiceProvider.GetRequiredService<CostSyncService>();
            var budgets = scope.ServiceProvider.GetRequiredService<BudgetEvaluator>();
            var reminders = scope.ServiceProvider.GetRequiredService<ReminderEvaluator>();

            await costSync.SyncDueAsync(ct);
            await budgets.EvaluateAllAsync(ct);
            await reminders.EvaluateAllAsync(ct);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Alert scheduler pass failed");
        }
    }
}
