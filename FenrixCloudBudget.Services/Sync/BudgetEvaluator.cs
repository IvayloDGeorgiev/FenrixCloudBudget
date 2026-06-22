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
                    Title: $"Budget alert: {budget.Project.Name} crossed {threshold}% (now {pct}%)",
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
        var services = await db.Services
            .Where(s => serviceId != null ? s.Id == serviceId : s.ProjectId == projectId)
            .Select(s => new
            {
                s.Id,
                s.EstimatedCost,
                s.Source,
                s.CloudAccountId
            })
            .ToListAsync(ct);
        if (services.Count == 0)
            return 0m;

        var ids = services.Select(s => s.Id).ToArray();
        var actualByService = await db.CostRecords
            .Where(c => ids.Contains(c.ServiceId) && c.Date >= from && c.Date <= to)
            .GroupBy(c => c.ServiceId)
            .Select(g => new { ServiceId = g.Key, Amount = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(x => x.ServiceId, x => x.Amount, ct);

        var accountIds = services
            .Where(s => s.CloudAccountId is not null)
            .Select(s => s.CloudAccountId!.Value)
            .Distinct()
            .ToArray();
        var syncedAccountIds = accountIds.Length == 0
            ? new HashSet<int>()
            : (await db.CloudAccounts
                .Where(a => accountIds.Contains(a.Id) && a.LastSyncedUtc != null)
                .Select(a => a.Id)
                .ToListAsync(ct))
                .ToHashSet();

        return services.Sum(service =>
        {
            if (actualByService.TryGetValue(service.Id, out var actual))
                return actual;

            if (service.Source == ServiceSource.Connected
                && service.CloudAccountId is { } accountId
                && syncedAccountIds.Contains(accountId))
                return 0m;

            return service.EstimatedCost;
        });
    }

    private static (DateOnly From, DateOnly To, string PeriodKey) CurrentPeriod()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        return (from, to, $"{today.Year}-{today.Month:00}");
    }
}
