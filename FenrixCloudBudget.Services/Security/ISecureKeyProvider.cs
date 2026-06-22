namespace FenrixCloudBudget.Services.Security;

/// <summary>
/// Supplies the AES master key used to encrypt secrets at rest. The MAUI host implements
/// this with SecureStorage (OS keychain/keystore); the master key never lives in SQLite.
/// A development fallback (file-based) is provided for non-MAUI hosts (tests / the API).
/// </summary>
public interface ISecureKeyProvider
{
    /// <summary>Return the 32-byte AES-256 key, creating &amp; persisting one on first use.</summary>
    Task<byte[]> GetOrCreateKeyAsync(CancellationToken ct = default);
}
