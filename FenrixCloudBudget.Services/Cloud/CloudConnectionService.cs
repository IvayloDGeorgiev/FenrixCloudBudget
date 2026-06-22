using System.Text.Json;
using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FenrixCloudBudget.Services.Cloud;

/// <summary>
/// Orchestrates cloud account connection, re-authentication, and resource discovery so the UI
/// stays thin. Secrets go through ISecretStore (encrypted); non-secret fields are persisted on
/// the CloudAccount as JSON so the connector can be rebuilt for later discovery/cost calls.
/// Depends only on the Core <see cref="ICloudConnectorFactory"/> abstraction — not on the Cloud project.
/// </summary>
public sealed class CloudConnectionService
{
    private readonly ICloudConnectorFactory _factory;
    private readonly ISecretStore _secrets;
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ILogger<CloudConnectionService> _log;

    public CloudConnectionService(
        ICloudConnectorFactory factory,
        ISecretStore secrets,
        IDbContextFactory<AppDbContext> dbf,
        ILogger<CloudConnectionService> log)
    {
        _factory = factory;
        _secrets = secrets;
        _dbf = dbf;
        _log = log;
    }

    /// <summary>Fields the connect form should render for a provider.</summary>
    public IReadOnlyList<CloudFieldSpec> SchemaFor(CloudProvider provider)
        => _factory.Create(provider).CredentialSchema;

    /// <summary>Validate credentials, store the secret, and persist a CloudAccount. Returns the saved account or an error.</summary>
    public async Task<ConnectResult> ConnectAsync(
        CloudProvider provider, string displayName, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        var connector = _factory.Create(provider);
        var validationError = ValidateFields(connector.CredentialSchema, fields);
        if (validationError is not null)
            return ConnectResult.Fail(validationError);

        var auth = await connector.AuthenticateAsync(new CloudCredential(provider, fields), ct);
        if (!auth.Success)
            return ConnectResult.Fail(auth.Error ?? "Authentication failed.");

        try
        {
            var scopeId = await ResolveScopeAsync(provider, fields, connector, ct);
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                await RemoveCredentialQuietlyAsync(auth.CredentialReference, ct);
                return ConnectResult.Fail(NoScopeMessage(provider));
            }

            // Persist only the non-secret fields; the secret already lives in the secure store.
            var secretKeys = connector.CredentialSchema.Where(s => s.IsSecret).Select(s => s.Key).ToHashSet();
            var nonSecret = fields.Where(kv => !secretKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim());

            var account = new CloudAccount
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"{provider} account" : displayName.Trim(),
                Provider = provider,
                AccountOrSubscriptionId = scopeId,
                CredentialReference = auth.CredentialReference,
                SecretHint = auth.SecretHint,
                OptionsJson = JsonSerializer.Serialize(nonSecret),
                IsEnabled = true
            };

            await using var db = await _dbf.CreateDbContextAsync(ct);
            db.CloudAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            return ConnectResult.Ok(account);
        }
        catch (Exception ex)
        {
            await RemoveCredentialQuietlyAsync(auth.CredentialReference, ct);
            _log.LogWarning(ex, "Connecting {Provider} account failed after authentication", provider);
            return ConnectResult.Fail(FriendlyError(provider, ex));
        }
    }

    /// <summary>Validate and update an existing cloud account, preserving its secret when no replacement is supplied.</summary>
    public async Task<ConnectResult> UpdateAsync(
        int accountId,
        string displayName,
        IReadOnlyDictionary<string, string> suppliedFields,
        CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.CloudAccounts.FirstOrDefaultAsync(item => item.Id == accountId, ct);
        if (account is null)
            return ConnectResult.Fail("Cloud account not found.");

        var connector = _factory.Create(account.Provider);
        var fields = suppliedFields.ToDictionary(pair => pair.Key, pair => pair.Value);
        var secretKeys = connector.CredentialSchema.Where(field => field.IsSecret).Select(field => field.Key).ToArray();
        foreach (var secretKey in secretKeys)
        {
            if (fields.TryGetValue(secretKey, out var replacement) && !string.IsNullOrWhiteSpace(replacement))
                continue;

            if (account.CredentialReference is null)
                return ConnectResult.Fail("The saved credential is missing. Enter a new secret to update this account.");

            var existingSecret = await _secrets.GetAsync(account.CredentialReference, ct);
            if (string.IsNullOrWhiteSpace(existingSecret))
                return ConnectResult.Fail("The saved credential could not be read. Enter a new secret to update this account.");

            fields[secretKey] = existingSecret;
        }

        var validationError = ValidateFields(connector.CredentialSchema, fields);
        if (validationError is not null)
            return ConnectResult.Fail(validationError);

        var oldCredentialReference = account.CredentialReference;
        var auth = await connector.AuthenticateAsync(new CloudCredential(account.Provider, fields), ct);
        if (!auth.Success)
            return ConnectResult.Fail(auth.Error ?? "Authentication failed.");

        try
        {
            var scopeId = await ResolveScopeAsync(account.Provider, fields, connector, ct);
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                if (!string.Equals(oldCredentialReference, auth.CredentialReference, StringComparison.Ordinal))
                    await RemoveCredentialQuietlyAsync(auth.CredentialReference, ct);
                return ConnectResult.Fail(NoScopeMessage(account.Provider));
            }

            var nonSecret = fields
                .Where(pair => !secretKeys.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());

            account.DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? $"{account.Provider} account"
                : displayName.Trim();
            account.AccountOrSubscriptionId = scopeId;
            account.OptionsJson = JsonSerializer.Serialize(nonSecret);
            account.CredentialReference = auth.CredentialReference;
            account.SecretHint = auth.SecretHint;
            account.IsEnabled = true;
            await db.SaveChangesAsync(ct);

            if (!string.IsNullOrWhiteSpace(oldCredentialReference)
                && !string.Equals(oldCredentialReference, auth.CredentialReference, StringComparison.Ordinal))
                await RemoveCredentialQuietlyAsync(oldCredentialReference, ct);

            return ConnectResult.Ok(account);
        }
        catch (Exception ex)
        {
            if (!string.Equals(oldCredentialReference, auth.CredentialReference, StringComparison.Ordinal))
                await RemoveCredentialQuietlyAsync(auth.CredentialReference, ct);
            _log.LogWarning(ex, "Updating cloud account {AccountId} failed after authentication", accountId);
            return ConnectResult.Fail(FriendlyError(account.Provider, ex));
        }
    }

    /// <summary>Delete a cloud account and its encrypted credential. Related services/reminders are retained with a null account link.</summary>
    public async Task<bool> DeleteAsync(int accountId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.CloudAccounts.FirstOrDefaultAsync(item => item.Id == accountId, ct);
        if (account is null)
            return false;

        var credentialReference = account.CredentialReference;
        db.CloudAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        await RemoveCredentialQuietlyAsync(credentialReference, ct);
        return true;
    }

    /// <summary>Rebuild an authenticated connector from a saved account (options JSON + stored secret).</summary>
    public async Task<ICloudConnector> ReauthenticateAsync(CloudAccount account, CancellationToken ct = default)
    {
        var connector = _factory.Create(account.Provider);

        var fields = string.IsNullOrWhiteSpace(account.OptionsJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(account.OptionsJson!) ?? new();

        var scopeKey = account.Provider switch
        {
            CloudProvider.Azure => "subscriptionId",
            CloudProvider.Gcp => "projectId",
            _ => null
        };
        if (scopeKey is not null
            && !fields.ContainsKey(scopeKey)
            && !string.IsNullOrWhiteSpace(account.AccountOrSubscriptionId))
            fields[scopeKey] = account.AccountOrSubscriptionId;

        var secretKey = connector.CredentialSchema.FirstOrDefault(s => s.IsSecret)?.Key;
        if (secretKey is not null && account.CredentialReference is not null)
        {
            var secret = await _secrets.GetAsync(account.CredentialReference, ct);
            if (secret is not null) fields[secretKey] = secret;
        }

        var auth = await connector.AuthenticateAsync(new CloudCredential(account.Provider, fields), ct);
        if (!auth.Success)
            throw new InvalidOperationException($"Re-authentication failed: {auth.Error}");

        // Connectors validate by using the supplied secret and return a newly stored handle.
        // During re-authentication the account already owns the canonical handle, so discard
        // the temporary copy to avoid adding an encrypted SecretEntry on every background sync.
        if (auth.CredentialReference is not null
            && !string.Equals(auth.CredentialReference, account.CredentialReference, StringComparison.Ordinal))
        {
            try
            {
                await _secrets.RemoveAsync(auth.CredentialReference, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not remove temporary credential created while re-authenticating account {AccountId}", account.Id);
            }
        }

        return connector;
    }

    /// <summary>Discover resources for a saved account so they can be grouped into a Project.</summary>
    public async Task<IReadOnlyList<DiscoveredResource>> DiscoverAsync(int accountId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.CloudAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new InvalidOperationException("Cloud account not found.");

        var connector = await ReauthenticateAsync(account, ct);
        var scopeId = account.AccountOrSubscriptionId;
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            scopeId = (await connector.ListScopesAsync(ct)).FirstOrDefault()?.Id;
            if (string.IsNullOrWhiteSpace(scopeId))
                throw new InvalidOperationException(NoScopeMessage(account.Provider));

            // Repair older account records that were saved before scope validation was enforced.
            account.AccountOrSubscriptionId = scopeId;
            await db.SaveChangesAsync(ct);
        }

        try
        {
            return await connector.DiscoverResourcesAsync(scopeId, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Resource discovery failed for cloud account {AccountId}", accountId);
            throw new InvalidOperationException(FriendlyError(account.Provider, ex), ex);
        }
    }

    private static string? ValidateFields(
        IReadOnlyList<CloudFieldSpec> schema,
        IReadOnlyDictionary<string, string> fields)
    {
        var missing = schema
            .Where(field => field.Required
                            && (!fields.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value)))
            .Select(field => field.Label)
            .ToArray();

        return missing.Length == 0
            ? null
            : $"Enter the required field{(missing.Length == 1 ? "" : "s")}: {string.Join(", ", missing)}.";
    }

    private static async Task<string?> ResolveScopeAsync(
        CloudProvider provider,
        IReadOnlyDictionary<string, string> fields,
        ICloudConnector connector,
        CancellationToken ct)
    {
        var explicitKey = provider switch
        {
            CloudProvider.Azure => "subscriptionId",
            CloudProvider.Gcp => "projectId",
            _ => null
        };

        if (explicitKey is not null
            && fields.TryGetValue(explicitKey, out var explicitScope)
            && !string.IsNullOrWhiteSpace(explicitScope))
            return explicitScope.Trim();

        return (await connector.ListScopesAsync(ct)).FirstOrDefault()?.Id;
    }

    private static string NoScopeMessage(CloudProvider provider) => provider switch
    {
        CloudProvider.Azure =>
            "No accessible Azure subscription was found. Enter the Subscription ID and assign the app registration's service principal the Reader and Cost Management Reader roles at that subscription.",
        CloudProvider.Gcp => "No accessible GCP project was found. Check the Project ID and service-account roles.",
        _ => "No accessible cloud account scope was found for these credentials."
    };

    private static string FriendlyError(CloudProvider provider, Exception exception)
    {
        if (exception is InvalidOperationException && !string.IsNullOrWhiteSpace(exception.Message))
            return exception.Message;

        return provider switch
        {
            CloudProvider.Azure =>
                "Azure could not validate this account. Check the Tenant ID, Client ID, Subscription ID, client secret, and subscription-level RBAC roles.",
            _ => exception.Message
        };
    }

    private async Task RemoveCredentialQuietlyAsync(string? reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return;

        try
        {
            await _secrets.RemoveAsync(reference, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not remove cloud credential {Reference}", reference);
        }
    }
}

public record ConnectResult(bool Success, CloudAccount? Account = null, string? Error = null)
{
    public static ConnectResult Ok(CloudAccount account) => new(true, account);
    public static ConnectResult Fail(string error) => new(false, null, error);
}
