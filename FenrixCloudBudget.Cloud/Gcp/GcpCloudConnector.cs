using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Asset.V1;
using Google.Cloud.BigQuery.V2;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Cloud.Gcp;

/// <summary>
/// GCP connector.
///   Inventory: Cloud Asset Inventory (SearchAllResources) — needs <c>Cloud Asset Viewer</c>.
///   Cost:      BigQuery billing export. The export table can live in a DIFFERENT project than the
///              resources, so the billing project, dataset and table are configured separately.
///              Querying needs <c>BigQuery Job User</c> (to run the query) on the billing project and
///              <c>BigQuery Data Viewer</c> on the export dataset.
///
/// Cost granularity depends on which export the user enabled:
///   • Standard export   (gcp_billing_export_v1_*)          → SERVICE-level costs.
///   • Detailed export   (gcp_billing_export_resource_v1_*) → RESOURCE-level costs (per resource.name).
/// Provide the detailed table name to get resource-level attribution.
///
/// Credential fields: projectId, serviceAccountJson, billingProject?, billingDataset?, billingTable?.
/// </summary>
public sealed class GcpCloudConnector : ICloudConnector
{
    private readonly ISecretStore _secrets;
    private readonly ILogger<GcpCloudConnector> _log;

    private GoogleCredential? _credential;
    private string? _projectId;
    private string? _billingProject;
    private string? _billingDataset;
    private string? _billingTable;

    public CloudProvider Provider => CloudProvider.Gcp;

    public IReadOnlyList<CloudFieldSpec> CredentialSchema => new[]
    {
        new CloudFieldSpec("projectId", "Project ID", Placeholder: "my-gcp-project"),
        new CloudFieldSpec("serviceAccountJson", "Service account key (JSON)", IsSecret: true),
        new CloudFieldSpec("billingProject", "Billing export project", Required: false, Placeholder: "defaults to Project ID"),
        new CloudFieldSpec("billingDataset", "Billing export dataset", Required: false, Placeholder: "billing_export"),
        new CloudFieldSpec("billingTable", "Billing export table", Required: false,
            Placeholder: "gcp_billing_export_v1_XXXXXX (or *_resource_v1_* for resource-level)")
    };

    public GcpCloudConnector(ISecretStore secrets, ILogger<GcpCloudConnector> log)
    {
        _secrets = secrets;
        _log = log;
    }

    public async Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default)
    {
        if (!credential.Fields.TryGetValue("projectId", out var projectId) || string.IsNullOrWhiteSpace(projectId))
            return new AuthResult(false, Error: "Project ID is required.");
        if (!credential.Fields.TryGetValue("serviceAccountJson", out var json) || string.IsNullOrWhiteSpace(json))
            return new AuthResult(false, Error: "Service account key (JSON) is required.");

        _projectId = projectId.Trim();
        _billingProject = Blank(credential.Fields.GetValueOrDefault("billingProject")) ?? _projectId;
        _billingDataset = Blank(credential.Fields.GetValueOrDefault("billingDataset"));
        _billingTable = Blank(credential.Fields.GetValueOrDefault("billingTable"));

        try
        {
            _credential = GoogleCredential.FromJson(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GCP service account JSON could not be parsed");
            return new AuthResult(false, Error: "The service account key isn't valid JSON. Paste the full key file contents.");
        }

        // Verify Cloud Asset access (not just that a client can be created).
        try
        {
            var asset = await new AssetServiceClientBuilder { Credential = _credential }.BuildAsync(ct);
            var page = asset.SearchAllResourcesAsync($"projects/{_projectId}", query: string.Empty, assetTypes: null);
            await page.AsRawResponses().GetAsyncEnumerator(ct).MoveNextAsync();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
        {
            return new AuthResult(false, Error:
                "The service account authenticated, but it lacks Cloud Asset access. Grant it the " +
                "\"Cloud Asset Viewer\" role on the project and enable the Cloud Asset Inventory API.");
        }
        catch (RpcException ex)
        {
            _log.LogWarning(ex, "GCP Cloud Asset validation failed ({Status})", ex.StatusCode);
            return new AuthResult(false, Error:
                "Could not reach Cloud Asset Inventory. Confirm the API is enabled and the Project ID is correct.");
        }

        // If a billing export is configured, verify BigQuery access too.
        if (_billingDataset is not null)
        {
            try
            {
                var bq = await BigQueryClient.CreateAsync(_billingProject, _credential);
                await bq.GetDatasetAsync(_billingDataset, cancellationToken: ct); // needs Data Viewer on the dataset
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "GCP BigQuery billing export validation failed");
                return new AuthResult(false, Error:
                    "The billing export dataset couldn't be read. Check the billing project/dataset and grant the service " +
                    "account \"BigQuery Job User\" on the billing project and \"BigQuery Data Viewer\" on the dataset.");
            }
        }

        var handle = await _secrets.SaveAsync(json, ct);
        return new AuthResult(true, handle.Reference, handle.Hint);
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
        var scope = string.IsNullOrWhiteSpace(scopeId) ? _projectId! : scopeId;

        AssetServiceClient asset;
        try
        {
            asset = await new AssetServiceClientBuilder { Credential = _credential }.BuildAsync(ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not initialise the Cloud Asset client. Check the service account key.", ex);
        }

        var results = new List<DiscoveredResource>();
        try
        {
            await foreach (var item in asset.SearchAllResourcesAsync($"projects/{scope}", query: string.Empty, assetTypes: null).WithCancellation(ct))
            {
                results.Add(new DiscoveredResource(
                    ExternalId: item.Name,
                    Name: string.IsNullOrWhiteSpace(item.DisplayName) ? ShortName(item.Name) : item.DisplayName,
                    ResourceType: item.AssetType,
                    Provider: CloudProvider.Gcp,
                    Region: string.IsNullOrWhiteSpace(item.Location) ? null : item.Location));
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
        {
            throw new InvalidOperationException(
                "GCP denied resource discovery. Grant the service account the \"Cloud Asset Viewer\" role on the project.", ex);
        }

        return results;
    }

    public async Task<IReadOnlyList<CostDatum>> GetCostsAsync(
        string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default)
    {
        EnsureAuth();
        if (to < from)
            throw new ArgumentOutOfRangeException(nameof(to), "The cost query end date must be on or after the start date.");

        // Billing export is opt-in; without it there is no cost data to query.
        if (_billingDataset is null)
            return Array.Empty<CostDatum>();

        var table = _billingTable ?? "gcp_billing_export_v1_*";
        var isDetailed = table.Contains("resource", StringComparison.OrdinalIgnoreCase);

        var bq = await BigQueryClient.CreateAsync(_billingProject, _credential);

        // Net cost = gross cost plus credits (credits are stored as negative amounts).
        var resourceSelect = isDetailed ? "ANY_VALUE(resource.name) AS resource_name," : "CAST(NULL AS STRING) AS resource_name,";
        var sql =
            $"SELECT DATE(usage_start_time) AS day, " +
            $"       service.description AS service_name, " +
            $"       {resourceSelect} " +
            $"       SUM(cost) + SUM(IFNULL((SELECT SUM(c.amount) FROM UNNEST(credits) c), 0)) AS amount, " +
            $"       ANY_VALUE(currency) AS currency " +
            $"FROM `{_billingProject}.{_billingDataset}.{table}` " +
            $"WHERE DATE(usage_start_time) BETWEEN @from AND @to " +
            $"GROUP BY day, service_name " +
            (isDetailed ? ", resource_name " : string.Empty) +
            $"ORDER BY day";

        var parameters = new[]
        {
            new BigQueryParameter("from", BigQueryDbType.Date, from.ToDateTime(TimeOnly.MinValue)),
            new BigQueryParameter("to", BigQueryDbType.Date, to.ToDateTime(TimeOnly.MinValue))
        };

        var data = new List<CostDatum>();
        try
        {
            var rows = await bq.ExecuteQueryAsync(sql, parameters, cancellationToken: ct);
            await foreach (var row in rows.GetRowsAsync().WithCancellation(ct))
            {
                if (row["day"] is not DateTime day) continue;
                var amount = row["amount"] is null ? 0m : Convert.ToDecimal(row["amount"]);
                data.Add(new CostDatum(
                    DateOnly.FromDateTime(day),
                    amount,
                    row["currency"]?.ToString() ?? "USD",
                    ServiceName: row["service_name"]?.ToString(),
                    ExternalResourceId: row["resource_name"]?.ToString()));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GCP BigQuery cost query failed for {Project}.{Dataset}.{Table}", _billingProject, _billingDataset, table);
            throw new InvalidOperationException(
                "The BigQuery billing query failed. Confirm the billing export table name and that the service account has " +
                "\"BigQuery Job User\" on the billing project and \"BigQuery Data Viewer\" on the dataset.", ex);
        }

        return data;
    }

    private void EnsureAuth()
    {
        if (_credential is null || string.IsNullOrWhiteSpace(_projectId))
            throw new InvalidOperationException("Call AuthenticateAsync before using the GCP connector.");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ShortName(string assetName)
    {
        var slash = assetName.LastIndexOf('/');
        return slash >= 0 && slash < assetName.Length - 1 ? assetName[(slash + 1)..] : assetName;
    }
}
