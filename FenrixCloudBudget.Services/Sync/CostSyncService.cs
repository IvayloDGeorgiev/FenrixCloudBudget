using System.Globalization;
using System.Text;
using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Models;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Cloud;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Services.Sync;

/// <summary>
/// Pulls provider costs into the local daily CostRecord cache.
/// Provider calls are serialized, skipped until due, and AWS is never polled automatically
/// more frequently than every 12 hours because Cost Explorer charges per request.
/// </summary>
public sealed class CostSyncService
{
    private const int RefreshWindowDays = 62;
    private const int MinimumAwsAutoSyncHours = 12;

    private readonly CloudConnectionService _connections;
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly TimeProvider _time;
    private readonly ILogger<CostSyncService> _log;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public CostSyncService(
        CloudConnectionService connections,
        IDbContextFactory<AppDbContext> dbf,
        TimeProvider time,
        ILogger<CostSyncService> log)
    {
        _connections = connections;
        _dbf = dbf;
        _time = time;
        _log = log;
    }

    public Task<CostSyncSummary> SyncDueAsync(CancellationToken ct = default)
        => SyncAllAsync(force: false, ct);

    /// <summary>
    /// Sync all enabled accounts. A forced pass is used by an explicit user refresh and bypasses
    /// the interval cache; automatic passes respect AppSetting.SyncIntervalHours.
    /// </summary>
    public async Task<CostSyncSummary> SyncAllAsync(bool force, CancellationToken ct = default)
    {
        await _syncGate.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);

            await using var db = await _dbf.CreateDbContextAsync(ct);
            var configuredHours = await db.AppSettings
                .Select(s => (int?)s.SyncIntervalHours)
                .FirstOrDefaultAsync(ct) ?? 12;
            configuredHours = Math.Clamp(configuredHours, 1, 24 * 30);

            var accounts = await db.CloudAccounts
                .AsNoTracking()
                .Where(a => a.IsEnabled)
                .OrderBy(a => a.Id)
                .ToListAsync(ct);

            var considered = accounts.Count;
            var attempted = 0;
            var succeeded = 0;
            var recordsWritten = 0;
            var unallocatedRows = 0;
            var errors = new List<string>();

            foreach (var account in accounts)
            {
                ct.ThrowIfCancellationRequested();

                var intervalHours = account.Provider == CloudProvider.Aws
                    ? Math.Max(configuredHours, MinimumAwsAutoSyncHours)
                    : configuredHours;
                var due = account.LastSyncedUtc is null
                          || account.LastSyncedUtc <= now.Subtract(TimeSpan.FromHours(intervalHours));
                if (!force && !due)
                    continue;

                // There is nowhere to persist account-level cost until at least one discovered
                // service has been assigned to a project. Avoid a paid AWS call in that state.
                var hasConnectedServices = await db.Services
                    .AnyAsync(s => s.CloudAccountId == account.Id && s.Source == ServiceSource.Connected, ct);
                if (!hasConnectedServices)
                {
                    _log.LogInformation("Skipping cost sync for account {AccountId}; it has no connected project services", account.Id);
                    continue;
                }

                attempted++;
                try
                {
                    var connector = await _connections.ReauthenticateAsync(account, ct);
                    var scopeId = account.AccountOrSubscriptionId
                                  ?? (await connector.ListScopesAsync(ct)).FirstOrDefault()?.Id
                                  ?? throw new InvalidOperationException("The cloud account has no queryable scope.");

                    var from = today.AddDays(-RefreshWindowDays);
                    var costs = await connector.GetCostsAsync(scopeId, from, today, CostGroupBy.Service, ct);
                    var persisted = await ReplaceWindowAsync(account.Id, from, today, costs, now, ct);
                    recordsWritten += persisted.RecordsWritten;
                    unallocatedRows += persisted.UnallocatedRows;
                    succeeded++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Cost sync failed for {Provider} account {AccountId}", account.Provider, account.Id);
                    errors.Add($"{account.DisplayName}: {ex.Message}");
                }
            }

            return new CostSyncSummary(
                considered,
                attempted,
                succeeded,
                recordsWritten,
                unallocatedRows,
                errors);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<PersistResult> ReplaceWindowAsync(
        int accountId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<CostDatum> costs,
        DateTimeOffset syncedUtc,
        CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var services = await db.Services
            .Where(s => s.CloudAccountId == accountId && s.Source == ServiceSource.Connected)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var account = await db.CloudAccounts.FirstAsync(a => a.Id == accountId, ct);
        var allocations = Allocate(costs, services, account.Provider, syncedUtc);
        var serviceIds = services.Select(s => s.Id).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (serviceIds.Length > 0)
        {
            await db.CostRecords
                .Where(c => serviceIds.Contains(c.ServiceId) && c.Date >= from && c.Date <= to)
                .ExecuteDeleteAsync(ct);
        }

        db.CostRecords.AddRange(allocations.Records);
        account.LastSyncedUtc = syncedUtc;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new PersistResult(allocations.Records.Count, allocations.UnallocatedRows);
    }

    private static AllocationResult Allocate(
        IReadOnlyList<CostDatum> costs,
        IReadOnlyList<Service> services,
        CloudProvider provider,
        DateTimeOffset syncedUtc)
    {
        var byExternalId = services
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalResourceId))
            .GroupBy(s => NormalizeResourceId(s.ExternalResourceId!))
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<(int ServiceId, DateOnly Date, string Currency), decimal>();
        var unallocated = 0;

        foreach (var cost in costs.Where(c => c.Date != default && c.Amount != 0))
        {
            IReadOnlyList<Service> matches = Array.Empty<Service>();
            if (!string.IsNullOrWhiteSpace(cost.ExternalResourceId)
                && byExternalId.TryGetValue(NormalizeResourceId(cost.ExternalResourceId), out var exact))
            {
                matches = exact;
            }
            else if (!string.IsNullOrWhiteSpace(cost.ServiceName))
            {
                matches = MatchByServiceName(cost.ServiceName, services, provider);
            }

            if (matches.Count == 0 && services.Count == 1)
                matches = services;

            if (matches.Count == 0)
            {
                unallocated++;
                continue;
            }

            var currency = string.IsNullOrWhiteSpace(cost.Currency)
                ? "USD"
                : cost.Currency.Trim().ToUpperInvariant();
            var share = decimal.Round(cost.Amount / matches.Count, 8, MidpointRounding.ToEven);
            var allocated = 0m;

            for (var i = 0; i < matches.Count; i++)
            {
                var amount = i == matches.Count - 1 ? cost.Amount - allocated : share;
                allocated += amount;
                var key = (matches[i].Id, cost.Date, currency);
                totals[key] = totals.GetValueOrDefault(key) + amount;
            }
        }

        var records = totals
            .OrderBy(x => x.Key.ServiceId)
            .ThenBy(x => x.Key.Date)
            .Select(x => new CostRecord
            {
                ServiceId = x.Key.ServiceId,
                Date = x.Key.Date,
                Amount = decimal.Round(x.Value, 2, MidpointRounding.ToEven),
                Currency = x.Key.Currency,
                Source = ServiceSource.Connected,
                SyncedUtc = syncedUtc
            })
            .Where(x => x.Amount != 0)
            .ToList();

        return new AllocationResult(records, unallocated);
    }

    private static IReadOnlyList<Service> MatchByServiceName(
        string providerServiceName,
        IReadOnlyList<Service> services,
        CloudProvider provider)
    {
        var costName = Normalize(providerServiceName);
        var costKey = ProviderServiceKey(provider, providerServiceName);

        return services.Where(service =>
        {
            var values = new[] { service.Name, service.ServiceType, service.ExternalResourceId }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();

            if (values.Any(value =>
                {
                    var normalized = Normalize(value);
                    return normalized == costName
                           || (normalized.Length >= 5 && costName.Contains(normalized, StringComparison.Ordinal))
                           || (costName.Length >= 5 && normalized.Contains(costName, StringComparison.Ordinal));
                }))
                return true;

            return costKey is not null
                   && values.Select(value => ProviderServiceKey(provider, value))
                       .Any(key => string.Equals(key, costKey, StringComparison.Ordinal));
        }).ToArray();
    }

    private static string? ProviderServiceKey(CloudProvider provider, string value)
    {
        var normalized = Normalize(value);
        if (provider == CloudProvider.Aws)
        {
            if (ContainsAny(normalized, "elasticcomputecloud", "amazonec2", "awsec2", "arnec2", "ec2")) return "ec2";
            if (ContainsAny(normalized, "simplestorageservice", "amazons3", "awss3", "arns3")) return "s3";
            if (ContainsAny(normalized, "relationaldatabaseservice", "amazonrds", "awsrds", "arnrds")) return "rds";
            if (ContainsAny(normalized, "awslambda", "amazonlambda", "arnlambda")) return "lambda";
            if (ContainsAny(normalized, "amazondynamodb", "awsdynamodb", "arndynamodb")) return "dynamodb";
            if (ContainsAny(normalized, "amazoncloudfront", "awscloudfront", "arncloudfront")) return "cloudfront";
        }

        if (provider == CloudProvider.Azure)
        {
            if (ContainsAny(normalized, "virtualmachines", "microsoftcompute")) return "virtualmachines";
            if (ContainsAny(normalized, "appservice", "microsoftweb")) return "appservice";
            if (ContainsAny(normalized, "azuresql", "microsoftsql")) return "sql";
            if (ContainsAny(normalized, "azurestorage", "microsoftstorage")) return "storage";
            if (ContainsAny(normalized, "cosmosdb", "microsoftdocumentdb")) return "cosmosdb";
        }

        if (provider == CloudProvider.Gcp)
        {
            if (ContainsAny(normalized, "cloudrun", "rungoogleapis")) return "cloudrun";
            if (ContainsAny(normalized, "computeengine", "computegoogleapis")) return "computeengine";
            if (ContainsAny(normalized, "cloudstorage", "storagegoogleapis")) return "cloudstorage";
            if (ContainsAny(normalized, "bigquery", "bigquerygoogleapis")) return "bigquery";
            if (ContainsAny(normalized, "cloudsql", "sqladmingoogleapis")) return "cloudsql";
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

    private static string NormalizeResourceId(string value)
        => value.Trim().TrimEnd('/').ToLowerInvariant();

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private sealed record AllocationResult(List<CostRecord> Records, int UnallocatedRows);
    private sealed record PersistResult(int RecordsWritten, int UnallocatedRows);
}

public sealed record CostSyncSummary(
    int AccountsConsidered,
    int AccountsAttempted,
    int AccountsSucceeded,
    int RecordsWritten,
    int UnallocatedRows,
    IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
