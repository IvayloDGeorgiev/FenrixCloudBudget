using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
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
    private const string ManagementScope = "https://management.azure.com/.default";
    private const string CostApiVersion = "2025-03-01";
    private const int MaxRetryAttempts = 4;

    private readonly ISecretStore _secrets;
    private readonly IHttpClientFactory _httpClients;
    private readonly ILogger<AzureCloudConnector> _log;
    private ArmClient? _arm;
    private ClientSecretCredential? _credential;

    public CloudProvider Provider => CloudProvider.Azure;

    public IReadOnlyList<CloudFieldSpec> CredentialSchema => new[]
    {
        new CloudFieldSpec("tenantId", "Tenant ID", Placeholder: "00000000-0000-0000-0000-000000000000"),
        new CloudFieldSpec("clientId", "Client ID (app registration)", Placeholder: "00000000-0000-0000-0000-000000000000"),
        new CloudFieldSpec("subscriptionId", "Subscription ID", Placeholder: "00000000-0000-0000-0000-000000000000"),
        new CloudFieldSpec("clientSecret", "Client secret", IsSecret: true)
    };

    public AzureCloudConnector(
        ISecretStore secrets,
        IHttpClientFactory httpClients,
        ILogger<AzureCloudConnector> log)
    {
        _secrets = secrets;
        _httpClients = httpClients;
        _log = log;
    }

    public async Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default)
    {
        try
        {
            if (!credential.Fields.TryGetValue("tenantId", out var tenant) || string.IsNullOrWhiteSpace(tenant))
                return new AuthResult(false, Error: "Tenant ID is required.");
            if (!credential.Fields.TryGetValue("clientId", out var clientId) || string.IsNullOrWhiteSpace(clientId))
                return new AuthResult(false, Error: "Client ID is required.");
            if (!credential.Fields.TryGetValue("subscriptionId", out var subscriptionId) || string.IsNullOrWhiteSpace(subscriptionId))
                return new AuthResult(false, Error: "Subscription ID is required.");
            if (!credential.Fields.TryGetValue("clientSecret", out var clientSecret) || string.IsNullOrWhiteSpace(clientSecret))
                return new AuthResult(false, Error: "Client secret is required.");

            if (!Guid.TryParse(tenant, out _))
                return new AuthResult(false, Error: "Tenant ID must be a valid GUID.");
            if (!Guid.TryParse(clientId, out _))
                return new AuthResult(false, Error: "Client ID must be a valid GUID.");
            if (!Guid.TryParse(subscriptionId, out _))
                return new AuthResult(false, Error: "Subscription ID must be a valid GUID.");

            _credential = new ClientSecretCredential(tenant, clientId, clientSecret);
            _arm = new ArmClient(_credential);

            // Authentication alone does not grant Azure resource access. Confirm that this service
            // principal can see the requested subscription before storing the credential.
            var subscriptionVisible = false;
            await foreach (var subscription in _arm.GetSubscriptions().GetAllAsync(ct))
            {
                if (string.Equals(subscription.Data.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase))
                {
                    subscriptionVisible = true;
                    break;
                }
            }

            if (!subscriptionVisible)
            {
                return new AuthResult(
                    false,
                    Error: "The app registration authenticated, but it cannot access that subscription. " +
                           "Assign its service principal the Reader role and Cost Management Reader role at the subscription scope, then try again.");
            }

            var handle = await _secrets.SaveAsync(clientSecret, ct);
            return new AuthResult(true, handle.Reference, handle.Hint);
        }
        catch (AuthenticationFailedException ex)
        {
            _log.LogWarning(ex, "Azure credential authentication failed");
            return new AuthResult(
                false,
                Error: "Azure authentication failed. Check the Tenant ID, Client ID, client-secret value, and whether the secret has expired.");
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _log.LogWarning(ex, "Azure subscription access validation failed");
            return new AuthResult(
                false,
                Error: "The credentials are valid, but Azure denied subscription access. Assign the service principal Reader and Cost Management Reader at the subscription scope.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Azure authentication failed");
            return new AuthResult(
                false,
                Error: "Azure could not validate the connection. Check the IDs, client secret, subscription access, and try again.");
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
        if (!Guid.TryParse(scopeId, out _))
        {
            throw new InvalidOperationException(
                "This Azure account has no valid Subscription ID. Edit the cloud account, enter the Azure subscription GUID, and validate it again.");
        }

        var tenant = _arm!.GetTenants().First();
        var query = new ResourceQueryContent(
            "Resources | project id, name, type, location, resourceGroup | limit 1000")
        {
            Subscriptions = { scopeId }
        };

        Response<ResourceQueryResult> resp;
        try
        {
            resp = await tenant.GetResourcesAsync(query, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            throw new InvalidOperationException(
                "Azure denied resource discovery. Assign the app registration's service principal the Reader role at the subscription scope.",
                ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            throw new InvalidOperationException(
                "Azure rejected the subscription scope. Check that the Subscription ID is correct and that the service principal can access it.",
                ex);
        }
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

    public async Task<IReadOnlyList<CostDatum>> GetCostsAsync(
        string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default)
    {
        EnsureAuth();
        if (to < from)
            throw new ArgumentOutOfRangeException(nameof(to), "The cost query end date must be on or after the start date.");

        var token = await _credential!.GetTokenAsync(
            new TokenRequestContext(new[] { ManagementScope }), ct);

        var groupingNames = groupBy switch
        {
            CostGroupBy.ResourceGroup => new[] { "ResourceGroup", "ResourceId" },
            CostGroupBy.Day => new[] { "ResourceId" },
            _ => new[] { "ServiceName", "ResourceId" }
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "ActualCost",
            timeframe = "Custom",
            timePeriod = new
            {
                from = $"{from:yyyy-MM-dd}T00:00:00Z",
                // Use an exclusive upper bound so every connector treats the public "to" value as inclusive.
                to = $"{to.AddDays(1):yyyy-MM-dd}T00:00:00Z"
            },
            dataset = new
            {
                granularity = "Daily",
                aggregation = new Dictionary<string, object>
                {
                    ["totalCost"] = new { name = "PreTaxCost", function = "Sum" }
                },
                grouping = groupingNames.Select(name => new { type = "Dimension", name }).ToArray()
            }
        });

        var scope = NormalizeManagementScope(scopeId);
        var next = new Uri(
            $"https://management.azure.com{scope}/providers/Microsoft.CostManagement/query?api-version={CostApiVersion}");
        var data = new List<CostDatum>();

        while (next is not null)
        {
            using var response = await SendWithRetryAsync(next, body, token.Token, ct);
            if (response.StatusCode == HttpStatusCode.NoContent)
                break;

            var json = await response.Content.ReadAsStringAsync(ct);
            var page = ParseCostPage(json, groupingNames[0]);
            data.AddRange(page.Data);
            next = ValidateNextLink(page.NextLink);
        }

        return data;
    }

    private void EnsureAuth()
    {
        if (_arm is null || _credential is null)
            throw new InvalidOperationException("Call AuthenticateAsync before using the Azure connector.");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Uri endpoint, byte[] body, string bearerToken, CancellationToken ct)
    {
        var client = _httpClients.CreateClient(nameof(AzureCloudConnector));

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is not HttpStatusCode.TooManyRequests and not HttpStatusCode.ServiceUnavailable)
            {
                if (response.IsSuccessStatusCode)
                    return response;

                var error = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                throw new HttpRequestException(
                    $"Azure Cost Management query failed with HTTP {(int)response.StatusCode}: {error}",
                    null,
                    response.StatusCode);
            }

            if (attempt >= MaxRetryAttempts)
            {
                var statusCode = response.StatusCode;
                var error = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                throw new HttpRequestException(
                    $"Azure Cost Management query exhausted retries with HTTP {(int)statusCode}: {error}",
                    null,
                    statusCode);
            }

            var delay = RetryDelay(response, attempt);
            _log.LogWarning(
                "Azure Cost Management throttled/unavailable ({Status}); retrying in {Delay} (attempt {Attempt}/{MaxAttempts})",
                (int)response.StatusCode,
                delay,
                attempt + 1,
                MaxRetryAttempts);
            response.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (TryHeaderSeconds(response, "x-ms-ratelimit-microsoft.consumption-retry-after", out var seconds))
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300));

        if (response.Headers.RetryAfter?.Delta is { } delta)
            return TimeSpan.FromSeconds(Math.Clamp(delta.TotalSeconds, 1, 300));

        if (response.Headers.RetryAfter?.Date is { } date)
            return TimeSpan.FromSeconds(Math.Clamp((date - DateTimeOffset.UtcNow).TotalSeconds, 1, 300));

        return TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt + 1)));
    }

    private static bool TryHeaderSeconds(HttpResponseMessage response, string name, out double seconds)
    {
        seconds = 0;
        return response.Headers.TryGetValues(name, out var values)
               && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
    }

    private static string NormalizeManagementScope(string scopeId)
    {
        var scope = scopeId.Trim();
        if (scope.StartsWith('/'))
            return scope.TrimEnd('/');

        if (scope.StartsWith("subscriptions/", StringComparison.OrdinalIgnoreCase)
            || scope.StartsWith("providers/", StringComparison.OrdinalIgnoreCase))
            return "/" + scope.TrimEnd('/');

        return $"/subscriptions/{Uri.EscapeDataString(scope)}";
    }

    private static Uri? ValidateNextLink(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
            return null;

        if (!Uri.TryCreate(nextLink, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("management.azure.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure Cost Management returned an invalid pagination URL.");

        return uri;
    }

    private static CostPage ParseCostPage(string json, string primaryGroupName)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("properties", out var properties))
            return new CostPage(Array.Empty<CostDatum>(), null);

        var columns = properties.TryGetProperty("columns", out var columnElement)
            ? columnElement.EnumerateArray()
                .Select((column, index) => new
                {
                    Name = column.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Index = index
                })
                .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var amountIndex = FirstColumn(columns, "totalCost", "PreTaxCost", "Cost", "CostInBillingCurrency", "CostUSD");
        var dateIndex = FirstColumn(columns, "UsageDate", "Date");
        var currencyIndex = FirstColumn(columns, "Currency", "BillingCurrencyCode");
        var serviceIndex = FirstColumn(columns, primaryGroupName, "ServiceName", "ResourceGroup");
        var resourceIndex = FirstColumn(columns, "ResourceId", "ResourceID");

        var results = new List<CostDatum>();
        if (amountIndex >= 0 && dateIndex >= 0 && properties.TryGetProperty("rows", out var rows))
        {
            foreach (var row in rows.EnumerateArray())
            {
                var cells = row.EnumerateArray().ToArray();
                if (!TryDecimal(cells, amountIndex, out var amount)
                    || !TryDate(cells, dateIndex, out var date))
                    continue;

                results.Add(new CostDatum(
                    date,
                    amount,
                    CellString(cells, currencyIndex) ?? "USD",
                    CellString(cells, serviceIndex),
                    CellString(cells, resourceIndex)));
            }
        }

        var nextLink = properties.TryGetProperty("nextLink", out var nextElement)
            ? nextElement.GetString()
            : null;
        return new CostPage(results, nextLink);
    }

    private static int FirstColumn(IReadOnlyDictionary<string, int> columns, params string[] names)
    {
        foreach (var name in names)
            if (columns.TryGetValue(name, out var index))
                return index;
        return -1;
    }

    private static string? CellString(JsonElement[] cells, int index)
    {
        if (index < 0 || index >= cells.Length || cells[index].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return cells[index].ValueKind == JsonValueKind.String
            ? cells[index].GetString()
            : cells[index].ToString();
    }

    private static bool TryDecimal(JsonElement[] cells, int index, out decimal value)
    {
        value = 0;
        if (index < 0 || index >= cells.Length)
            return false;

        return cells[index].ValueKind == JsonValueKind.Number
            ? decimal.TryParse(cells[index].GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            : decimal.TryParse(CellString(cells, index), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDate(JsonElement[] cells, int index, out DateOnly date)
    {
        date = default;
        var raw = CellString(cells, index);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return DateOnly.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private sealed record CostPage(IReadOnlyList<CostDatum> Data, string? NextLink);
}
