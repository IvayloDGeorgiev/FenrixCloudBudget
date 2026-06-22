using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Models;

/// <summary>A subscription/account scope the user can pick after connecting.</summary>
public record CloudScope(string Id, string DisplayName, CloudProvider Provider, string? ParentId = null);

/// <summary>A discovered resource available to add to a Project.</summary>
public record DiscoveredResource(
    string ExternalId,
    string Name,
    string ResourceType,
    CloudProvider Provider,
    string? Region = null,
    string? ResourceGroup = null,
    IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>A single cost figure returned by a provider's cost API.</summary>
public record CostDatum(
    DateOnly Date,
    decimal Amount,
    string Currency,
    string? ServiceName = null,
    string? ExternalResourceId = null);

/// <summary>How cost results should be grouped.</summary>
public enum CostGroupBy { Service, ResourceGroup, Day }

/// <summary>Credentials supplied by the user to authenticate a connector (transient — not persisted as-is).</summary>
public record CloudCredential(
    CloudProvider Provider,
    IReadOnlyDictionary<string, string> Fields);
