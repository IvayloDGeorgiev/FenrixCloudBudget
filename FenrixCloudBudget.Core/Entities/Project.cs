using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>A logical grouping of cloud Services across providers/accounts (the core "grouping" feature).</summary>
public class Project : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    /// <summary>Default reporting currency for this project (ISO 4217, e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<Budget> Budgets { get; set; } = new List<Budget>();

    /// <summary>Many-to-many link to the cloud accounts this project draws resources from.</summary>
    public ICollection<ProjectCloudAccount> CloudAccounts { get; set; } = new List<ProjectCloudAccount>();
}

/// <summary>Join entity: Project *—* CloudAccount.</summary>
public class ProjectCloudAccount
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int CloudAccountId { get; set; }
    public CloudAccount CloudAccount { get; set; } = null!;
}
