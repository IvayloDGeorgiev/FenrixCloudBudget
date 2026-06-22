using System.Globalization;
using Amazon;
using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using Amazon.ResourceGroupsTaggingAPI;
using Amazon.ResourceGroupsTaggingAPI.Model;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Cloud.Aws;

/// <summary>
/// AWS connector.
///   Inventory: Resource Groups Tagging API (GetResources), paginated and run across regions.
///   Cost:      Cost Explorer GetCostAndUsage. Cost Explorer is a GLOBAL service whose endpoint
///              lives in us-east-1, so the client is always pinned there regardless of the
///              resource region. (~$0.01 per paginated request, ~24h data lag — cache aggressively.)
///
/// Credential fields: accessKeyId, secretAccessKey, region (optional default for discovery).
/// The account scope is the real 12-digit account ID resolved via STS GetCallerIdentity — not a region.
///
/// Note: the Tagging API only returns *tagged* resources. Untagged resources won't appear in
/// discovery, but they are still fully captured in Cost Explorer spend (which is account-wide).
/// </summary>
public sealed class AwsCloudConnector : ICloudConnector
{
    // Commercial regions swept during discovery so resources outside the default region aren't missed.
    private static readonly string[] DiscoveryRegions =
    {
        "us-east-1", "us-east-2", "us-west-1", "us-west-2",
        "ca-central-1",
        "eu-west-1", "eu-west-2", "eu-west-3", "eu-central-1", "eu-north-1", "eu-south-1",
        "ap-south-1", "ap-southeast-1", "ap-southeast-2", "ap-northeast-1", "ap-northeast-2", "ap-northeast-3",
        "sa-east-1"
    };

    private readonly ISecretStore _secrets;
    private readonly ILogger<AwsCloudConnector> _log;

    private AWSCredentials? _creds;
    private RegionEndpoint _defaultRegion = RegionEndpoint.USEast1;
    private string? _accountId;

    public CloudProvider Provider => CloudProvider.Aws;

    public IReadOnlyList<CloudFieldSpec> CredentialSchema => new[]
    {
        new CloudFieldSpec("accessKeyId", "Access key ID", Placeholder: "AKIA…"),
        new CloudFieldSpec("secretAccessKey", "Secret access key", IsSecret: true),
        new CloudFieldSpec("region", "Default region", Required: false, Placeholder: "us-east-1")
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
            if (!credential.Fields.TryGetValue("accessKeyId", out var accessKey) || string.IsNullOrWhiteSpace(accessKey))
                return new AuthResult(false, Error: "Access key ID is required.");
            if (!credential.Fields.TryGetValue("secretAccessKey", out var secretKey) || string.IsNullOrWhiteSpace(secretKey))
                return new AuthResult(false, Error: "Secret access key is required.");

            _defaultRegion = ResolveRegion(credential.Fields.GetValueOrDefault("region"));
            _creds = new BasicAWSCredentials(accessKey.Trim(), secretKey.Trim());

            // Validate the credential and capture the real account ID. STS is global (us-east-1).
            using var sts = new AmazonSecurityTokenServiceClient(_creds, RegionEndpoint.USEast1);
            var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct);
            _accountId = identity.Account;

            var handle = await _secrets.SaveAsync(secretKey, ct);
            return new AuthResult(true, handle.Reference, handle.Hint);
        }
        catch (AmazonSecurityTokenServiceException ex) when (
            ex.ErrorCode is "InvalidClientTokenId" or "SignatureDoesNotMatch" or "AccessDenied")
        {
            _log.LogWarning(ex, "AWS credential validation failed ({Code})", ex.ErrorCode);
            return new AuthResult(false, Error:
                "AWS rejected these credentials. Check the access key ID and secret access key are correct and active.");
        }
        catch (AmazonServiceException ex)
        {
            _log.LogWarning(ex, "AWS authentication failed ({Code})", ex.ErrorCode);
            return new AuthResult(false, Error:
                "AWS could not validate the connection. Check the keys, region and the IAM permissions, then try again.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AWS authentication failed");
            return new AuthResult(false, Error: "AWS could not validate the connection. Check the credentials and try again.");
        }
    }

    public async Task<IReadOnlyList<CloudScope>> ListScopesAsync(CancellationToken ct = default)
    {
        EnsureAuth();
        if (string.IsNullOrWhiteSpace(_accountId))
        {
            using var sts = new AmazonSecurityTokenServiceClient(_creds, RegionEndpoint.USEast1);
            var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct);
            _accountId = identity.Account;
        }

        return new[] { new CloudScope(_accountId!, $"AWS account {_accountId}", CloudProvider.Aws) };
    }

    public async Task<IReadOnlyList<DiscoveredResource>> DiscoverResourcesAsync(string scopeId, CancellationToken ct = default)
    {
        EnsureAuth();

        // The Tagging API is regional and only returns tagged resources, so sweep every region
        // and de-duplicate by ARN to catch resources outside the default region.
        var byArn = new Dictionary<string, DiscoveredResource>(StringComparer.OrdinalIgnoreCase);

        foreach (var regionName in DiscoveryRegions)
        {
            ct.ThrowIfCancellationRequested();
            var region = RegionEndpoint.GetBySystemName(regionName);
            using var client = new AmazonResourceGroupsTaggingAPIClient(_creds, region);

            string? token = null;
            do
            {
                GetResourcesResponse resp;
                try
                {
                    resp = await client.GetResourcesAsync(
                        new GetResourcesRequest { PaginationToken = token, ResourcesPerPage = 100 }, ct);
                }
                catch (AmazonServiceException ex) when (IsRegionUnavailable(ex))
                {
                    // Region not enabled for this account — skip it.
                    break;
                }

                foreach (var mapping in resp.ResourceTagMappingList)
                {
                    var arn = mapping.ResourceARN;
                    if (string.IsNullOrWhiteSpace(arn) || byArn.ContainsKey(arn))
                        continue;

                    byArn[arn] = new DiscoveredResource(
                        ExternalId: arn,
                        Name: ResourceNameFromArn(arn),
                        ResourceType: ServiceFromArn(arn),
                        Provider: CloudProvider.Aws,
                        Region: regionName,
                        Tags: mapping.Tags?.ToDictionary(t => t.Key, t => t.Value));
                }

                token = string.IsNullOrEmpty(resp.PaginationToken) ? null : resp.PaginationToken;
            }
            while (token is not null && !ct.IsCancellationRequested);
        }

        return byArn.Values.OrderBy(r => r.ResourceType).ThenBy(r => r.Name).ToList();
    }

    public async Task<IReadOnlyList<CostDatum>> GetCostsAsync(
        string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default)
    {
        EnsureAuth();
        if (to < from)
            throw new ArgumentOutOfRangeException(nameof(to), "The cost query end date must be on or after the start date.");

        // Cost Explorer is a GLOBAL service — its endpoint is in us-east-1 regardless of where resources live.
        using var client = new AmazonCostExplorerClient(_creds, RegionEndpoint.USEast1);

        var dimension = groupBy == CostGroupBy.ResourceGroup ? "RESOURCE_ID" : "SERVICE";
        var request = new GetCostAndUsageRequest
        {
            TimePeriod = new DateInterval
            {
                Start = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                // Cost Explorer treats End as EXCLUSIVE, so add a day to make the public "to" inclusive.
                End = to.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            Granularity = Granularity.DAILY,
            Metrics = new List<string> { "UnblendedCost" },
            GroupBy = new List<GroupDefinition>
            {
                new() { Type = GroupDefinitionType.DIMENSION, Key = dimension }
            }
        };

        var data = new List<CostDatum>();
        string? nextToken = null;
        do
        {
            request.NextPageToken = nextToken;
            var resp = await client.GetCostAndUsageAsync(request, ct);

            foreach (var period in resp.ResultsByTime)
            {
                if (!DateOnly.TryParse(period.TimePeriod.Start, CultureInfo.InvariantCulture, out var date))
                    continue;

                foreach (var group in period.Groups)
                {
                    if (!group.Metrics.TryGetValue("UnblendedCost", out var metric))
                        continue;
                    if (!decimal.TryParse(metric.Amount, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
                        continue;

                    data.Add(new CostDatum(
                        date,
                        amount,
                        string.IsNullOrWhiteSpace(metric.Unit) ? "USD" : metric.Unit,
                        ServiceName: group.Keys.FirstOrDefault()));
                }
            }

            nextToken = resp.NextPageToken;
        }
        while (!string.IsNullOrEmpty(nextToken) && !ct.IsCancellationRequested);

        return data;
    }

    private void EnsureAuth()
    {
        if (_creds is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the AWS connector.");
    }

    private static RegionEndpoint ResolveRegion(string? region)
        => string.IsNullOrWhiteSpace(region) ? RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(region.Trim());

    private static bool IsRegionUnavailable(AmazonServiceException ex)
        => ex.ErrorCode is "UnrecognizedClientException" or "AuthFailure" or "OptInRequired"
           || ex.StatusCode == System.Net.HttpStatusCode.Forbidden;

    // arn:partition:service:region:account:resourcetype/resourceid (or :resourceid)
    private static string ServiceFromArn(string arn)
    {
        var parts = arn.Split(':');
        return parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : "aws";
    }

    private static string ResourceNameFromArn(string arn)
    {
        var lastSegment = arn.Split(':').LastOrDefault() ?? arn;
        var slash = lastSegment.LastIndexOf('/');
        return slash >= 0 && slash < lastSegment.Length - 1 ? lastSegment[(slash + 1)..] : lastSegment;
    }
}
