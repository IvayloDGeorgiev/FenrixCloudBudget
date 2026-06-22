using Amazon;
using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using Amazon.ResourceGroupsTaggingAPI;
using Amazon.ResourceGroupsTaggingAPI.Model;
using Amazon.Runtime;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Cloud.Aws;

/// <summary>
/// AWS connector.
///   Inventory: Resource Groups Tagging API (GetResources).
///   Cost:      Cost Explorer GetCostAndUsage, grouped by SERVICE.
/// NOTE: Cost Explorer charges ~$0.01 per paginated request and current-month data
/// lags ~24h — callers should cache aggressively and avoid frequent refreshes.
/// Credential fields expected: accessKeyId, secretAccessKey, region.
/// </summary>
public sealed class AwsCloudConnector : ICloudConnector
{
    private readonly ISecretStore _secrets;
    private readonly ILogger<AwsCloudConnector> _log;
    private AWSCredentials? _creds;
    private RegionEndpoint _region = RegionEndpoint.USEast1;

    public CloudProvider Provider => CloudProvider.Aws;

    public IReadOnlyList<CloudFieldSpec> CredentialSchema => new[]
    {
        new CloudFieldSpec("accessKeyId", "Access key ID", Placeholder: "AKIA..."),
        new CloudFieldSpec("secretAccessKey", "Secret access key", IsSecret: true),
        new CloudFieldSpec("region", "Region", Required: false, Placeholder: "us-east-1")
    };

    public AwsCloudConnector(ISecretStore secrets, ILogger<AwsCloudConnector> log)
    {
        _secrets = secrets;
        _log = log;
    }

    public async Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default)
    {
        try
        {
            var accessKey = credential.Fields["accessKeyId"];
            var secretKey = credential.Fields["secretAccessKey"];
            if (credential.Fields.TryGetValue("region", out var r) && !string.IsNullOrWhiteSpace(r))
                _region = RegionEndpoint.GetBySystemName(r);

            _creds = new BasicAWSCredentials(accessKey, secretKey);

            // Validate by making one cheap call.
            using var client = new AmazonResourceGroupsTaggingAPIClient(_creds, _region);
            await client.GetResourcesAsync(new GetResourcesRequest { ResourcesPerPage = 1 }, ct);

            var handle = await _secrets.SaveAsync(secretKey, ct);
            return new AuthResult(true, handle.Reference, handle.Hint);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AWS authentication failed");
            return new AuthResult(false, Error: ex.Message);
        }
    }

    public Task<IReadOnlyList<CloudScope>> ListScopesAsync(CancellationToken ct = default)
    {
        // A set of AWS credentials maps to a single account; multi-account (Organizations)
        // discovery can be added later. Surface the caller identity as the scope.
        IReadOnlyList<CloudScope> scopes = new[]
        {
            new CloudScope(_region.SystemName, $"AWS account ({_region.SystemName})", CloudProvider.Aws)
        };
        return Task.FromResult(scopes);
    }

    public async Task<IReadOnlyList<DiscoveredResource>> DiscoverResourcesAsync(string scopeId, CancellationToken ct = default)
    {
        EnsureAuth();
        var results = new List<DiscoveredResource>();
        using var client = new AmazonResourceGroupsTaggingAPIClient(_creds, _region);

        string? token = null;
        do
        {
            var resp = await client.GetResourcesAsync(new GetResourcesRequest { PaginationToken = token, ResourcesPerPage = 100 }, ct);
            foreach (var m in resp.ResourceTagMappingList)
            {
                var arn = m.ResourceARN ?? string.Empty;
                results.Add(new DiscoveredResource(
                    ExternalId: arn,
                    Name: arn.Split(':').LastOrDefault() ?? arn,
                    ResourceType: arn.Split(':').Skip(2).FirstOrDefault() ?? "aws",
                    Provider: CloudProvider.Aws,
                    Region: _region.SystemName,
                    Tags: m.Tags?.ToDictionary(t => t.Key, t => t.Value)));
            }
            token = string.IsNullOrEmpty(resp.PaginationToken) ? null : resp.PaginationToken;
        } while (token is not null && !ct.IsCancellationRequested);

        return results;
    }

    public async Task<IReadOnlyList<CostDatum>> GetCostsAsync(
        string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default)
    {
        EnsureAuth();
        using var client = new AmazonCostExplorerClient(_creds, _region);

        var request = new GetCostAndUsageRequest
        {
            // Cost Explorer's end date is exclusive; ICloudConnector exposes an inclusive window.
            TimePeriod = new DateInterval { Start = from.ToString("yyyy-MM-dd"), End = to.AddDays(1).ToString("yyyy-MM-dd") },
            Granularity = Granularity.DAILY,
            Metrics = new List<string> { "UnblendedCost" },
            GroupBy = new List<GroupDefinition>
            {
                new() { Type = GroupDefinitionType.DIMENSION, Key = "SERVICE" }
            }
        };

        var data = new List<CostDatum>();
        string? token = null;
        do
        {
            request.NextPageToken = token;
            var resp = await client.GetCostAndUsageAsync(request, ct);
            foreach (var period in resp.ResultsByTime)
            {
                var date = DateOnly.Parse(period.TimePeriod.Start);
                foreach (var group in period.Groups)
                {
                    var amount = group.Metrics["UnblendedCost"];
                    data.Add(new CostDatum(
                        date,
                        decimal.Parse(amount.Amount, System.Globalization.CultureInfo.InvariantCulture),
                        amount.Unit,
                        ServiceName: group.Keys.FirstOrDefault()));
                }
            }
            token = string.IsNullOrWhiteSpace(resp.NextPageToken) ? null : resp.NextPageToken;
        } while (token is not null && !ct.IsCancellationRequested);

        return data;
    }

    private void EnsureAuth()
    {
        if (_creds is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the AWS connector.");
    }
}
