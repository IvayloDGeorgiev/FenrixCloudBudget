using System.Text.Json;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Email;

/// <summary>
/// Gives email adapters access to the active EmailConfig's non-secret fields
/// (OptionsJson) and its decrypted secrets (via ISecretStore). One per scope so a
/// freshly saved config is always reflected.
/// </summary>
public sealed class EmailAdapterContext
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ISecretStore _secrets;

    public EmailAdapterContext(IDbContextFactory<AppDbContext> dbf, ISecretStore secrets)
    {
        _dbf = dbf;
        _secrets = secrets;
    }

    /// <summary>Non-secret method fields (host, port, region, fromAddress, ...).</summary>
    public async Task<Dictionary<string, string>> GetOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var cfg = await db.EmailConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var opts = string.IsNullOrWhiteSpace(cfg?.OptionsJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(cfg!.OptionsJson!) ?? new();

        if (!string.IsNullOrWhiteSpace(cfg?.FromAddress)) opts["fromAddress"] = cfg!.FromAddress!;
        if (!string.IsNullOrWhiteSpace(cfg?.FromName)) opts["fromName"] = cfg!.FromName!;
        return opts;
    }

    /// <summary>
    /// Resolve a secret for the active config. Adapters with a single secret can ignore
    /// <paramref name="fieldKey"/>; it exists for adapters that store more than one.
    /// </summary>
    public async Task<string> GetSecretAsync(string fieldKey, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var cfg = await db.EmailConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg?.CredentialReference is null) return string.Empty;
        return await _secrets.GetAsync(cfg.CredentialReference, ct) ?? string.Empty;
    }
}
