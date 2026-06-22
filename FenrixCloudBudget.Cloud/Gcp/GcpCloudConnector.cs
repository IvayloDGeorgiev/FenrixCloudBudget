using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using Google.Cloud.Asset.V1;
using Google.Cloud.BigQuery.V2;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Cloud.Gcp;

/// <summary>
/// GCP connector.
///   Inventory: Cloud Asset Inventory (SearchAllResources).
///   Cost:      BigQuery billing export (preferred). The user must first enable billing
///              export to a BigQuery dataset (an onboarding wizard guides this); the app
///              then runs SQL against that dataset.
/// Credential fields expected: projectId, serviceAccountJson, billingDataset (optional).
/// </summary>
public sealed class GcpCloudConnector : ICloudConnector
{
    private readonly ISecretStore _secrets;
    private readonly ILogger<GcpCloudConnector> _log;
    private string? _projectId;
    private string? _serviceAccountJson;
    private string? _billingDataset;

    public CloudProvider Provider => CloudProvider.Gcp;

    public GcpCloudConnector(ISecretStore secrets, ILogger<GcpCloudConnector> log)
    {
        _secrets = secrets;
        _log = log;
    }

    public async Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default)
    {
        try
        {
            _projectId = credential.Fields["projectId"];
            _serviceAccountJson = credential.Fields["serviceAccountJson"];
            credential.Fields.TryGetValue("billingDataset", out _billingDataset);

            // Validate the service-account JSON can build a BigQuery client.
            _ = await BigQueryClient.CreateAsync(_projectId,
                Google.Apis.Auth.OAuth2.GoogleCredential.FromJson(_serviceAccountJson));

            var handle = await _secrets.SaveAsync(_serviceAccountJson, ct);
            return new AuthResult(true, handle.Reference, handle.Hint);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GCP authentication failed");
            return new AuthResult(false, Error: ex.Message);
        }
    }

    public Task<IReadOnlyList<CloudScope>> ListScopesAsync(CancellationToken ct = default)
    {
        EnsureAuth();
        IReadOnlyList<CloudScope> scopes = new[]
        {
            new CloudScope(_projectId!, $"GCP project {_projectId}", CloudProvider.Gcp)
        };
        return Task.FromResult(scopes);
    }

    public async Task<IReadOnlyList<DiscoveredResource>> DiscoverResourcesAsync(string scopeId, CancellationToken ct = default)
    {
        EnsureAuth();
        var client = await new AssetServiceClientBuilder
        {
            JsonCredentials = _serviceAccountJson
        }.BuildAsync(ct);

        var results = new List<DiscoveredResource>();
        var request = new SearchAllResourcesRequest { Scope = $"projects/{scopeId}" };
        await foreach (var asset in client.SearchAllResourcesAsync(request).WithCancellation(ct))
        {
            results.Add(new DiscoveredResource(
                ExternalId: asset.Name,
                Name: asset.DisplayName ?? asset.Name,
                ResourceType: asset.AssetType,
                Provider: CloudProvider.Gcp,
                Region: asset.Location));
        }
        return results;
    }

    public async Task<IReadOnlyList<CostDatum>> GetCostsAsync(
        string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default)
    {
        EnsureAuth();
        if (string.IsNullOrWhiteSpace(_billingDataset))
            return Array.Empty<CostDatum>(); // billing export not configured yet (run the wizard)

        var client = await BigQueryClient.CreateAsync(_projectId,
            Google.Apis.Auth.OAuth2.GoogleCredential.FromJson(_serviceAccountJson));

        // Standard billing-export schema: gcp_billing_export_v1_*.
        var sql = $@"
SELECT DATE(usage_start_time) AS day,
       service.description    AS service_name,
       SUM(cost)              AS amount,
       ANY_VALUE(currency)    AS currency
FROM `{_projectId}.{_billingDataset}.gcp_billing_export_v1_*`
WHERE DATE(usage_start_time) BETWEEN @from AND @to
GROUP BY day, service_name
ORDER BY day";

        var parameters = new[]
        {
            new BigQueryParameter("from", BigQueryDbType.Date, from.ToDateTime(TimeOnly.MinValue)),
            new BigQueryParameter("to",   BigQueryDbType.Date, to.ToDateTime(TimeOnly.MinValue))
        };

        var data = new List<CostDatum>();
        var rows = await client.ExecuteQueryAsync(sql, parameters, cancellationToken: ct);
        foreach (var row in rows)
        {
            data.Add(new CostDatum(
                DateOnly.FromDateTime((DateTime)row["day"]),
                Convert.ToDecimal(row["amount"]),
                (string)row["currency"],
                ServiceName: row["service_name"]?.ToString()));
        }
        return data;
    }

    private void EnsureAuth()
    {
        if (_serviceAccountJson is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the GCP connector.");
    }
}
