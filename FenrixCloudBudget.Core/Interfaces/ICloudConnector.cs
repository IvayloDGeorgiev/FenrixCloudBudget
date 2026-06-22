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

    /// <summary>
    /// Fields the connect-account UI renders for this provider (some marked secret).
    /// Mirrors the email-adapter pattern: the UI builds the form dynamically from this schema.
    /// </summary>
    IReadOnlyList<CloudFieldSpec> CredentialSchema { get; }

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

/// <summary>Describes one credential field for a connector (drives the dynamic connect form).</summary>
public record CloudFieldSpec(string Key, string Label, bool IsSecret = false, bool Required = true, string? Placeholder = null);

/// <summary>Resolves the right connector for a provider (DI factory).</summary>
public interface ICloudConnectorFactory
{
    ICloudConnector Create(CloudProvider provider);
}
