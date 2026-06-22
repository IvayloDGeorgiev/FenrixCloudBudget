using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Analytics;

/// <summary>
/// Computes every dashboard metric in one pass: budget pacing &amp; forecast, projected month-end,
/// days-to-exhaust, month-over-month, budget health, spend trend + anomalies, cost composition
/// over time, by-client / by-project ranks, top movers, a treemap and data-quality split.
/// Spend = synced CostRecords where available, otherwise the manual estimate (matching the rest
/// of the app); time-series use synced records only.
/// </summary>
public sealed class DashboardAnalytics
{
    private readonly IDbContextFactory<AppDbContext> _dbf;

    public DashboardAnalytics(IDbContextFactory<AppDbContext> dbf) => _dbf = dbf;

    public async Task<DashboardData> GetAsync(DashboardQuery q, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var from = q.From <= q.To ? q.From : q.To;
        var to = q.From <= q.To ? q.To : q.From;

        // ---- Services in scope ----
        var services = db.Services.Include(s => s.Project).ThenInclude(p => p!.Client).AsQueryable();
        if (q.Provider != "All" && Enum.TryParse<CloudProvider>(q.Provider, out var prov))
            services = services.Where(s => s.Provider == prov);
        if (q.ClientId is not null)
            services = services.Where(s => s.Project.ClientId == q.ClientId);
        var list = await services.ToListAsync(ct);
        var serviceIds = list.Select(s => s.Id).ToHashSet();

        // ---- Cost records we need (range, current month, previous month) ----
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var prevMonthStart = monthStart.AddMonths(-1);
        var earliest = new[] { from, prevMonthStart }.Min();

        var allCosts = serviceIds.Count == 0
            ? new List<CostRecord>()
            : await db.CostRecords
                .Where(c => serviceIds.Contains(c.ServiceId) && c.Date >= earliest && c.Date <= to)
                .ToListAsync(ct);

        var svcProject = list.ToDictionary(s => s.Id, s => s.ProjectId);
        var svcProvider = list.ToDictionary(s => s.Id, s => s.Provider);

        var rangeCosts = allCosts.Where(c => c.Date >= from && c.Date <= to).ToList();
        var rangeActualByService = rangeCosts.GroupBy(c => c.ServiceId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        // Synced accounts (for "0 when synced but no rows" handling, mirrors existing behaviour).
        var accountIds = list.Where(s => s.CloudAccountId is not null).Select(s => s.CloudAccountId!.Value).Distinct().ToArray();
        var syncedAccounts = accountIds.Length == 0
            ? new Dictionary<int, DateTimeOffset>()
            : await db.CloudAccounts.Where(a => accountIds.Contains(a.Id) && a.LastSyncedUtc != null)
                .ToDictionaryAsync(a => a.Id, a => a.LastSyncedUtc!.Value, ct);

        decimal SpendFor(Service s)
        {
            if (rangeActualByService.TryGetValue(s.Id, out var actual)) return actual;
            if (s.Source == ServiceSource.Connected && s.CloudAccountId is { } id && syncedAccounts.ContainsKey(id)) return 0m;
            return s.EstimatedCost;
        }

        // ---- Breakdowns (range) ----
        var byProvider = list.GroupBy(s => s.Provider.ToString())
            .ToDictionary(g => g.Key, g => (double)g.Sum(SpendFor));

        var topServices = list.GroupBy(s => s.Name)
            .Select(g => new { g.Key, V = g.Sum(SpendFor) })
            .OrderByDescending(x => x.V).Take(6)
            .ToDictionary(x => x.Key, x => (double)x.V);

        var byProject = list.GroupBy(s => s.Project.Name)
            .Select(g => new RankItem(g.Key, (double)g.Sum(SpendFor)))
            .OrderByDescending(r => r.Value).Take(8).ToList();

        var byClient = list.GroupBy(s => s.Project.Client?.Name ?? "No client")
            .Select(g => new RankItem(g.Key, (double)g.Sum(SpendFor)))
            .OrderByDescending(r => r.Value).Take(8).ToList();

        var treemap = list
            .Select(s => new TreeLeaf(s.Project.Name, s.Name, (double)SpendFor(s), s.Provider.ToString()))
            .Where(t => t.Value > 0)
            .OrderByDescending(t => t.Value).Take(16).ToList();

        // ---- Spend trend + anomalies (range, synced only) ----
        var trendGroups = rangeCosts.GroupBy(c => c.Date).OrderBy(g => g.Key).ToList();
        var trend = trendGroups.Select(g => new KeyValuePair<string, double>(g.Key.ToString("MMM d"), (double)g.Sum(c => c.Amount))).ToList();
        var anomalies = DetectAnomalies(trend.Select(t => t.Value).ToList());

        // ---- Composition over time by provider (range, synced only) ----
        var providersInData = rangeCosts.Select(c => svcProvider.GetValueOrDefault(c.ServiceId).ToString()).Distinct().OrderBy(x => x).ToList();
        var composition = rangeCosts.GroupBy(c => c.Date).OrderBy(g => g.Key)
            .Select(g => new CompositionDay(
                g.Key.ToString("MMM d"),
                g.GroupBy(c => svcProvider.GetValueOrDefault(c.ServiceId).ToString())
                 .ToDictionary(pg => pg.Key, pg => (double)pg.Sum(c => c.Amount))))
            .ToList();

        // ---- Per-project sparklines (range, synced only) ----
        var sparks = rangeCosts.GroupBy(c => svcProject.GetValueOrDefault(c.ServiceId, -1))
            .Where(g => g.Key > 0)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<double>)g.GroupBy(c => c.Date).OrderBy(d => d.Key).Select(d => (double)d.Sum(c => c.Amount)).ToList());

        // ---- Top movers: current range vs previous equal-length range (synced) ----
        var len = to.DayNumber - from.DayNumber + 1;
        var prevFrom = from.AddDays(-len);
        var prevTo = from.AddDays(-1);
        var prevCosts = serviceIds.Count == 0
            ? new List<CostRecord>()
            : await db.CostRecords.Where(c => serviceIds.Contains(c.ServiceId) && c.Date >= prevFrom && c.Date <= prevTo).ToListAsync(ct);
        var curByName = rangeCosts.GroupBy(c => ServiceName(list, c.ServiceId)).ToDictionary(g => g.Key, g => (double)g.Sum(c => c.Amount));
        var prevByName = prevCosts.GroupBy(c => ServiceName(list, c.ServiceId)).ToDictionary(g => g.Key, g => (double)g.Sum(c => c.Amount));
        var movers = curByName.Keys.Union(prevByName.Keys)
            .Select(n => new MoverItem(n, curByName.GetValueOrDefault(n), prevByName.GetValueOrDefault(n),
                curByName.GetValueOrDefault(n) - prevByName.GetValueOrDefault(n)))
            .Where(m => Math.Abs(m.Delta) > 0.005)
            .OrderByDescending(m => Math.Abs(m.Delta)).Take(6).ToList();

        // ---- Budgets + health ----
        var budgetQuery = db.Budgets.Include(b => b.Project).ThenInclude(p => p!.Client).Where(b => b.IsEnabled);
        if (q.ClientId is not null) budgetQuery = budgetQuery.Where(b => b.Project.ClientId == q.ClientId);
        var budgets = await budgetQuery.ToListAsync(ct);
        var budgetRows = budgets.Select(b =>
        {
            var spend = list.Where(s => s.ProjectId == b.ProjectId).Sum(SpendFor);
            var pct = b.Amount > 0 ? (int)Math.Round(spend / b.Amount * 100) : 0;
            return new BudgetHealthRow(b.ProjectId, b.Project.Name, b.Amount, spend, pct);
        }).ToList();

        // ---- Current-month pacing & forecast ----
        var monthlyBudget = budgets.Sum(NormalizeMonthly);
        var elapsed = Math.Max(1, today.Day);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var monthCosts = allCosts.Where(c => c.Date >= monthStart && c.Date <= today).ToList();
        var mtd = (double)monthCosts.Sum(c => c.Amount);
        var dailyRate = mtd / elapsed;
        var projected = dailyRate * daysInMonth;
        var idealToDate = monthlyBudget * elapsed / daysInMonth;   // on-budget pace so far

        // cumulative actual per day-of-month
        var perDay = new double[daysInMonth + 1];
        foreach (var c in monthCosts) perDay[c.Date.Day] += (double)c.Amount;
        var cum = 0d;
        var pacing = new List<PacingPoint>();
        for (var d = 1; d <= daysInMonth; d++)
        {
            if (d <= elapsed) cum += perDay[d];
            double ideal = monthlyBudget * d / daysInMonth;
            double? actual = d <= elapsed ? cum : null;
            double? forecast = d >= elapsed ? mtd + dailyRate * (d - elapsed) : null;
            pacing.Add(new PacingPoint(d, ideal, actual, forecast));
        }

        int? daysToExhaust = null;
        if (dailyRate > 0 && monthlyBudget > 0 && projected > monthlyBudget)
        {
            var dayHit = (int)Math.Ceiling(monthlyBudget / dailyRate); // day-of-month budget is hit
            daysToExhaust = Math.Max(0, dayHit - elapsed);
        }

        // Month-over-month: same-elapsed comparison for fairness.
        var prevMonthSameElapsed = allCosts.Where(c => c.Date >= prevMonthStart && c.Date <= prevMonthStart.AddDays(elapsed - 1)).Sum(c => (double)c.Amount);
        var momPct = prevMonthSameElapsed > 0 ? (mtd - prevMonthSameElapsed) / prevMonthSameElapsed * 100 : 0;

        // ---- Data quality ----
        var syncedTotal = rangeCosts.Sum(c => c.Amount);
        var estimatedTotal = list.Where(s => !rangeActualByService.ContainsKey(s.Id)).Sum(s => s.EstimatedCost);

        var lastSynced = syncedAccounts.Count > 0
            ? $"Cloud costs last synced {Relative(syncedAccounts.Values.Max())}. Unsynced services use estimates."
            : "Showing manual estimates — connect and sync a cloud account for actuals.";

        return new DashboardData
        {
            TotalSpend = list.Sum(SpendFor),
            DataMode = rangeCosts.Count > 0 ? "Synced actuals" : "Estimated",
            LastSyncedLabel = lastSynced,
            MonthToDateSpend = (decimal)mtd,
            MonthlyBudget = (decimal)monthlyBudget,
            ProjectedMonthEnd = (decimal)projected,
            DaysLeftInMonth = daysInMonth - elapsed,
            DaysUntilBudgetExhausted = daysToExhaust,
            PrevMonthSpend = (decimal)prevMonthSameElapsed,
            MoMChangePercent = momPct,
            Pacing = pacing,
            PaceVariance = (decimal)(mtd - idealToDate),
            BudgetsOnTrack = budgetRows.Count(r => r.Percent < 80),
            BudgetsApproaching = budgetRows.Count(r => r.Percent is >= 80 and < 100),
            BudgetsOver = budgetRows.Count(r => r.Percent >= 100),
            Budgets = budgetRows,
            ProjectSparklines = sparks,
            SpendTrend = trend,
            AnomalyIndices = anomalies,
            Composition = composition,
            CompositionProviders = providersInData,
            ByProvider = byProvider,
            TopServices = topServices,
            ByProject = byProject,
            ByClient = byClient,
            TopMovers = movers,
            Treemap = treemap,
            SyncedActualTotal = syncedTotal,
            EstimatedTotal = estimatedTotal,
            ProjectCount = list.Select(s => s.ProjectId).Distinct().Count(),
            ConnectedServiceCount = list.Count(s => s.Source == ServiceSource.Connected),
            ProviderCount = list.Select(s => s.Provider).Distinct().Count()
        };
    }

    private static string ServiceName(List<Service> list, int id) => list.FirstOrDefault(s => s.Id == id)?.Name ?? "Unknown";

    private static double NormalizeMonthly(Budget b) => b.Period switch
    {
        BudgetPeriod.Quarterly => (double)b.Amount / 3,
        BudgetPeriod.Annual => (double)b.Amount / 12,
        _ => (double)b.Amount
    };

    /// <summary>Flags days whose spend exceeds mean + 2·stddev (simple statistical anomaly).</summary>
    private static List<int> DetectAnomalies(List<double> values)
    {
        var result = new List<int>();
        if (values.Count < 4) return result;
        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        if (sd <= 0) return result;
        for (var i = 0; i < values.Count; i++)
            if (values[i] > mean + 2 * sd) result.Add(i);
        return result;
    }

    private static string Relative(DateTimeOffset value)
    {
        var age = DateTimeOffset.UtcNow - value;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)age.TotalMinutes)} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)age.TotalHours)} hr ago";
        return $"{Math.Max(1, (int)age.TotalDays)} day(s) ago";
    }
}
