namespace FenrixCloudBudget.Services.Analytics;

/// <summary>Filter for the dashboard analytics query.</summary>
public record DashboardQuery(string Provider, int? ClientId, DateOnly From, DateOnly To);

/// <summary>One point on the budget-pacing chart for the current month.</summary>
public record PacingPoint(int Day, double Ideal, double? Actual, double? Forecast);

/// <summary>A daily breakdown of spend per provider (for the stacked composition chart).</summary>
public record CompositionDay(string Label, IReadOnlyDictionary<string, double> ByProvider);

/// <summary>A ranked spend item (by client or project).</summary>
public record RankItem(string Label, double Value);

/// <summary>A biggest-mover item: change vs the previous comparable period.</summary>
public record MoverItem(string Label, double Current, double Previous, double Delta);

/// <summary>A treemap leaf (project → service), sized by spend and coloured by provider.</summary>
public record TreeLeaf(string Group, string Name, double Value, string Provider);

public record BudgetHealthRow(int ProjectId, string Project, decimal Budget, decimal Spend, int Percent);

/// <summary>Everything the dashboard needs, computed in one pass.</summary>
public record DashboardData
{
    // Headline (respects provider/client filter; month-based where noted)
    public decimal TotalSpend { get; init; }
    public string DataMode { get; init; } = "Estimated";
    public string LastSyncedLabel { get; init; } = "";

    // Current-month pacing & forecast
    public decimal MonthToDateSpend { get; init; }
    public decimal MonthlyBudget { get; init; }
    public decimal ProjectedMonthEnd { get; init; }
    public int DaysLeftInMonth { get; init; }
    public int? DaysUntilBudgetExhausted { get; init; }   // null = won't exhaust this month at current rate
    public decimal PrevMonthSpend { get; init; }
    public double MoMChangePercent { get; init; }
    public IReadOnlyList<PacingPoint> Pacing { get; init; } = Array.Empty<PacingPoint>();

    /// <summary>Month-to-date spend minus the on-budget pace to date. Negative = under budget pace (good).</summary>
    public decimal PaceVariance { get; init; }

    // Budget health
    public int BudgetsOnTrack { get; init; }
    public int BudgetsApproaching { get; init; }
    public int BudgetsOver { get; init; }
    public IReadOnlyList<BudgetHealthRow> Budgets { get; init; } = Array.Empty<BudgetHealthRow>();
    public IReadOnlyDictionary<int, IReadOnlyList<double>> ProjectSparklines { get; init; } =
        new Dictionary<int, IReadOnlyList<double>>();

    // Composition / trends (range based)
    public IReadOnlyList<KeyValuePair<string, double>> SpendTrend { get; init; } = Array.Empty<KeyValuePair<string, double>>();
    public IReadOnlyList<int> AnomalyIndices { get; init; } = Array.Empty<int>();
    public IReadOnlyList<CompositionDay> Composition { get; init; } = Array.Empty<CompositionDay>();
    public IReadOnlyList<string> CompositionProviders { get; init; } = Array.Empty<string>();

    // Breakdowns (range based)
    public IReadOnlyDictionary<string, double> ByProvider { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> TopServices { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<RankItem> ByProject { get; init; } = Array.Empty<RankItem>();
    public IReadOnlyList<RankItem> ByClient { get; init; } = Array.Empty<RankItem>();
    public IReadOnlyList<MoverItem> TopMovers { get; init; } = Array.Empty<MoverItem>();
    public IReadOnlyList<TreeLeaf> Treemap { get; init; } = Array.Empty<TreeLeaf>();

    // Data quality
    public decimal SyncedActualTotal { get; init; }
    public decimal EstimatedTotal { get; init; }

    public int ProjectCount { get; init; }
    public int ConnectedServiceCount { get; init; }
    public int ProviderCount { get; init; }
}
