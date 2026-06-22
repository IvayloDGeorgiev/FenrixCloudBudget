using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>A single cloud service/resource belonging to a Project. Either manually entered or discovered (connected).</summary>
public class Service : EntityBase
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public CloudProvider Provider { get; set; }

    /// <summary>Service type / SKU, e.g. "EC2 t3.medium", "Azure SQL S1", "GCP Cloud Run".</summary>
    public string? ServiceType { get; set; }
    public string? Tier { get; set; }            // subscription/plan tier

    public ServiceSource Source { get; set; } = ServiceSource.Manual;

    /// <summary>External resource id when Source = Connected (ARN, Azure resource id, GCP asset name).</summary>
    public string? ExternalResourceId { get; set; }

    public int? CloudAccountId { get; set; }
    public CloudAccount? CloudAccount { get; set; }

    /// <summary>Manually estimated recurring cost per billing period.</summary>
    public decimal EstimatedCost { get; set; }
    public BudgetPeriod EstimatePeriod { get; set; } = BudgetPeriod.Monthly;
    public string Currency { get; set; } = "USD";

    public ICollection<CostRecord> CostRecords { get; set; } = new List<CostRecord>();
}
