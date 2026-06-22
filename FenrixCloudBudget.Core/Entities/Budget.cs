using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>A spend limit for a Project (or optionally a single Service) over a period, with alert thresholds.</summary>
public class Budget : EntityBase
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>Optional: scope the budget to a single service rather than the whole project.</summary>
    public int? ServiceId { get; set; }
    public Service? Service { get; set; }

    public string Name { get; set; } = string.Empty;
    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Percent thresholds that trigger alerts, comma-separated, e.g. "50,80,100".</summary>
    public string Thresholds { get; set; } = "50,80,100";

    public bool IsEnabled { get; set; } = true;

    public IEnumerable<int> ThresholdPercents() =>
        (Thresholds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0);
}
