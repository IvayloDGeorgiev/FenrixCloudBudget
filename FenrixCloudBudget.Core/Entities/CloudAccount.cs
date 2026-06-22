using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Entities;

/// <summary>A connected cloud account/subscription. Credentials are stored encrypted and referenced, never in plaintext.</summary>
public class CloudAccount : EntityBase
{
    public string DisplayName { get; set; } = string.Empty;
    public CloudProvider Provider { get; set; }

    /// <summary>AWS account id, Azure subscription id, or GCP project/billing id.</summary>
    public string? AccountOrSubscriptionId { get; set; }

    /// <summary>Optional narrower scope (resource group, region, billing dataset).</summary>
    public string? Scope { get; set; }

    /// <summary>Opaque key into the secure credential store (OS keychain/keystore). NOT the secret itself.</summary>
    public string? CredentialReference { get; set; }

    /// <summary>First 4 chars of the client secret/key for masked display ("abcd••••••••").</summary>
    public string? SecretHint { get; set; }

    public DateTimeOffset? LastSyncedUtc { get; set; }
    public bool IsEnabled { get; set; } = true;

    public ICollection<ProjectCloudAccount> Projects { get; set; } = new List<ProjectCloudAccount>();
    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();
}
