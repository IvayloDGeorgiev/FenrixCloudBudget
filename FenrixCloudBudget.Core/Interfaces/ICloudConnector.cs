using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Models;

namespace FenrixCloudBudget.Core.Interfaces;

/// <summary>
/// Provider-agnostic cloud integration. One implementation per provider
/// (AWS / Azure / GCP) keeps the rest of the app provider-neutral.
/// </summary>
public interface ICloudConnector
{
    CloudProvider Provider { get; }

    /// <summary>Validate &amp; securely store the supplied credential. Returns a SecretHint for masked display.</summary>
    Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default);

    /// <summary>List subscriptions/accounts available under the stored credential.</summary>
    Task<IReadOnlyList<CloudScope>> ListScopesAsync(CancellationToken ct = default);

    /// <summary>Inventory resources within a scope so the user can group them into a Project.</summary>
    Task<IReadOnlyList<DiscoveredResource>> DiscoverResourcesAsync(string scopeId, CancellationToken ct = default);

    /// <summary>Near-real-time cost data for a scope and date window.</summary>
    Task<IReadOnlyList<CostDatum>> GetCostsAsync(
        string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default);
}

public record AuthResult(bool Success, string? CredentialReference = null, string? SecretHint = null, string? Error = null);

/// <summary>Resolves the right connector for a provider (DI factory).</summary>
public interface ICloudConnectorFactory
{
    ICloudConnector Create(CloudProvider provider);
}
