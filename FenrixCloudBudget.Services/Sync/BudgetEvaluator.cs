using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Sync;

/// <summary>
/// Evaluates every enabled budget against current-period spend and fires threshold alerts
/// (50/80/100% etc.) through the shared notification service. Spend = synced CostRecords if
/// present, otherwise the sum of manual service estimates — so it works from Phase 1 (manual)
/// and gets sharper in Phase 4 (synced).
/// </summary>
public sealed class BudgetEvaluator
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly INotificationService _notifications;

    public BudgetEvaluator(IDbContextFactory<AppDbContext> dbf, INotificationService notifications)
    {
        _dbf = dbf;
        _notifications = notifications;
    }

    public async Task EvaluateAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var budgets = await db.Budgets
            .Where(b => b.IsEnabled)
            .Include(b => b.Project)
            .ToListAsync(ct);

        var (from, to, periodKey) = CurrentPeriod();

        foreach (var budget in budgets)
        {
            if (budget.Amount <= 0) continue;

            var spend = await CurrentSpendAsync(db, budget.ProjectId, budget.ServiceId, from, to, ct);
            var pct = (int)Math.Floor(spend / budget.Amount * 100m);

            foreach (var threshold in budget.ThresholdPercents().OrderByDescending(t => t))
            {
                if (pct < threshold) continue;

                var dedupe = $"budget:{budget.Id}:{threshold}:{periodKey}";
                await _notifications.DispatchAsync(new NotificationRequest(
                    NotificationSourceType.Budget,
                    budget.Id,
                    NotificationChannel.InApp | NotificationChannel.LocalDevice | NotificationChannel.Email,
                    Title: $"Budget alert: {budget.Project.Name} at {pct}%",
                    Message: $"{budget.Project.Name} reached {pct}% of its {budget.Period} budget ({spend:0.##}/{budget.Amount:0.##} {budget.Currency}).",
                    EmailTo: null, // resolved to the account owner / admin in server mode
                    EmailTemplateKey: Email.Templates.EmailTemplateRenderer.BudgetBreach,
                    EmailData: new
                    {
                        ProjectName = budget.Project.Name,
                        Percent = pct,
                        Period = budget.Period.ToString(),
                        Spend = spend.ToString("0.##"),
                        Amount = budget.Amount.ToString("0.##"),
                        budget.Currency
                    },
                    DedupeKey: dedupe), ct);

                break; // fire only the highest crossed threshold per period
            }
        }
    }

    private static async Task<decimal> CurrentSpendAsync(
        AppDbContext db, int projectId, int? serviceId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var costs = db.CostRecords.Where(c => c.Date >= from && c.Date <= to);
        costs = serviceId is not null
            ? costs.Where(c => c.ServiceId == serviceId)
            : costs.Where(c => c.Service.ProjectId == projectId);

        var synced = await costs.SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
        if (synced > 0) return synced;

        // Fall back to manual estimates (monthly-normalised) when no synced data exists yet.
        var services = db.Services.Where(s => serviceId != null ? s.Id == serviceId : s.ProjectId == projectId);
        return await services.SumAsync(s => (decimal?)s.EstimatedCost, ct) ?? 0m;
    }

    private static (DateOnly From, DateOnly To, string PeriodKey) CurrentPeriod()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        return (from, to, $"{today.Year}-{today.Month:00}");
    }
}
