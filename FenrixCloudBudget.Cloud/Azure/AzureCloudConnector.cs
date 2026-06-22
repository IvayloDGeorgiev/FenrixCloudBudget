using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Cloud.Azure;

/// <summary>
/// Azure connector.
///   Inventory: Azure Resource Graph (KQL) for fast cross-subscription listing.
///   Cost:      Cost Management Query API (/query). Strict rate limits — batch + cache,
///              back off on HTTP 429. Requires the "Cost Management Reader" role.
/// Credential fields expected: tenantId, clientId, clientSecret (app registration).
/// </summary>
public sealed class AzureCloudConnector : ICloudConnector
{
    private readonly ISecretStore _secrets;
    private readonly ILogger<AzureCloudConnector> _log;
    private ArmClient? _arm;
    private ClientSecretCredential? _credential;

    public CloudProvider Provider => CloudProvider.Azure;

    public IReadOnlyList<CloudFieldSpec> CredentialSchema => new[]
    {
        new CloudFieldSpec("tenantId", "Tenant ID"),
        new CloudFieldSpec("clientId", "Client ID (app registration)"),
        new CloudFieldSpec("clientSecret", "Client secret", IsSecret: true)
    };

    public AzureCloudConnector(ISecretStore secrets, ILogger<AzureCloudConnector> log)
    {
        _secrets = secrets;
        _log = log;
    }

    public async Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default)
    {
        try
        {
            var tenant = credential.Fields["tenantId"];
            var clientId = credential.Fields["clientId"];
            var clientSecret = credential.Fields["clientSecret"];

            _credential = new ClientSecretCredential(tenant, clientId, clientSecret);
            _arm = new ArmClient(_credential);

            // Validate by enumerating subscriptions.
            await foreach (var _ in _arm.GetSubscriptions().GetAllAsync(ct)) break;

            var handle = await _secrets.SaveAsync(clientSecret, ct);
            return new AuthResult(true, handle.Reference, handle.Hint);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Azure authentication failed");
            return new AuthResult(false, Error: ex.Message);
        }
    }

    public async Task<IReadOnlyList<CloudScope>> ListScopesAsync(CancellationToken ct = default)
    {
        EnsureAuth();
        var scopes = new List<CloudScope>();
        await foreach (var sub in _arm!.GetSubscriptions().GetAllAsync(ct))
        {
            scopes.Add(new CloudScope(
                sub.Data.SubscriptionId ?? sub.Id!.ToString(),
                sub.Data.DisplayName ?? "Subscription",
                CloudProvider.Azure));
        }
        return scopes;
    }

    public async Task<IReadOnlyList<DiscoveredResource>> DiscoverResourcesAsync(string scopeId, CancellationToken ct = default)
    {
        EnsureAuth();
        var tenant = _arm!.GetTenants().First();
        var query = new ResourceQueryContent(
            "Resources | project id, name, type, location, resourceGroup | limit 1000")
        {
            Subscriptions = { scopeId }
        };

        var resp = await tenant.GetResourcesAsync(query, ct);
        var results = new List<DiscoveredResource>();
        // The /resources payload is dynamic JSON; map the projected columns.
        foreach (var row in System.Text.Json.JsonSerializer
                     .Deserialize<List<Dictionary<string, object>>>(resp.Value.Data.ToString()) ?? new())
        {
            results.Add(new DiscoveredResource(
                ExternalId: row.GetValueOrDefault("id")?.ToString() ?? string.Empty,
                Name: row.GetValueOrDefault("name")?.ToString() ?? string.Empty,
                ResourceType: row.GetValueOrDefault("type")?.ToString() ?? "azure",
                Provider: CloudProvider.Azure,
                Region: row.GetValueOrDefault("location")?.ToString(),
                ResourceGroup: row.GetValueOrDefault("resourceGroup")?.ToString()));
        }
        return results;
    }

    public Task<IReadOnlyList<CostDatum>> GetCostsAsync(
        string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default)
    {
        EnsureAuth();
        // TODO(Phase 4): POST to
        //   /subscriptions/{scopeId}/providers/Microsoft.CostManagement/query?api-version=2025-03-01
        // body groups by ResourceGroup/ServiceName, granularity Daily. Use _credential for the
        // bearer token, honor rate limits (429 -> exponential back-off), and cache results.
        IReadOnlyList<CostDatum> empty = Array.Empty<CostDatum>();
        return Task.FromResult(empty);
    }

    private void EnsureAuth()
    {
        if (_arm is null) throw new InvalidOperationException("Call AuthenticateAsync before using the Azure connector.");
    }
}
