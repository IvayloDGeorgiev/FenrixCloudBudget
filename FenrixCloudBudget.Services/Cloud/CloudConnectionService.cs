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

        var auth = await connector.AuthenticateAsync(new CloudCredential(provider, fields), ct);
        if (!auth.Success)
            return ConnectResult.Fail(auth.Error ?? "Authentication failed.");

        string? scopeId = null;
        try
        {
            var scopes = await connector.ListScopesAsync(ct);
            scopeId = scopes.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ListScopes failed after auth for {Provider}", provider);
        }

        // Persist only the non-secret fields; the secret already lives in the secure store.
        var secretKeys = connector.CredentialSchema.Where(s => s.IsSecret).Select(s => s.Key).ToHashSet();
        var nonSecret = fields.Where(kv => !secretKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        var account = new CloudAccount
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"{provider} account" : displayName,
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

    /// <summary>Rebuild an authenticated connector from a saved account (options JSON + stored secret).</summary>
    public async Task<ICloudConnector> ReauthenticateAsync(CloudAccount account, CancellationToken ct = default)
    {
        var connector = _factory.Create(account.Provider);

        var fields = string.IsNullOrWhiteSpace(account.OptionsJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(account.OptionsJson!) ?? new();

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
        var scopeId = account.AccountOrSubscriptionId
            ?? (await connector.ListScopesAsync(ct)).FirstOrDefault()?.Id
            ?? string.Empty;

        return await connector.DiscoverResourcesAsync(scopeId, ct);
    }
}

public record ConnectResult(bool Success, CloudAccount? Account = null, string? Error = null)
{
    public static ConnectResult Ok(CloudAccount account) => new(true, account);
    public static ConnectResult Fail(string error) => new(false, null, error);
}
