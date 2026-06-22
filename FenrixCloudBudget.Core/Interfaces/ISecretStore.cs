namespace FenrixCloudBudget.Core.Interfaces;

/// <summary>
/// Secure storage for secrets (cloud client secrets, email API keys, connection strings).
/// Backed by OS secure storage (keychain/keystore via MAUI SecureStorage) with AES at rest.
/// Returns an opaque reference stored on entities; the secret is never round-tripped to the UI.
/// </summary>
public interface ISecretStore
{
    /// <summary>Persist a secret; returns an opaque reference and a first-4-then-masked hint.</summary>
    Task<SecretHandle> SaveAsync(string secret, CancellationToken ct = default);

    Task<string?> GetAsync(string reference, CancellationToken ct = default);

    Task RemoveAsync(string reference, CancellationToken ct = default);

    /// <summary>Build the "abcd••••••••" display hint from a raw secret.</summary>
    static string Mask(string secret)
        => string.IsNullOrEmpty(secret)
            ? string.Empty
            : (secret.Length <= 4 ? secret : secret[..4]) + new string('•', Math.Min(12, Math.Max(4, secret.Length - 4)));
}

public record SecretHandle(string Reference, string Hint);
