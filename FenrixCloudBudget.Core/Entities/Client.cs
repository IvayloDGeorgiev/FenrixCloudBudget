namespace FenrixCloudBudget.Core.Entities;

/// <summary>A customer the developer/agency bills for. Projects belong to a Client.</summary>
public class Client : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingEmail { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }   // comma-separated

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
