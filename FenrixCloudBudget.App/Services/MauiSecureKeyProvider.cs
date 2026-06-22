using System.Security.Cryptography;
using FenrixCloudBudget.Services.Security;

namespace FenrixCloudBudget.App.Services;

/// <summary>
/// Production ISecureKeyProvider for MAUI: stores the AES master key in OS secure storage
/// (Keychain on Apple, KeyStore on Android, DPAPI-backed on Windows) via MAUI SecureStorage.
/// </summary>
public sealed class MauiSecureKeyProvider : ISecureKeyProvider
{
    private const string KeyName = "fenrix.master.key";
    private byte[]? _cached;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<byte[]> GetOrCreateKeyAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null) return _cached;

            var existing = await SecureStorage.Default.GetAsync(KeyName);
            if (!string.IsNullOrEmpty(existing))
            {
                _cached = Convert.FromBase64String(existing);
                return _cached;
            }

            _cached = RandomNumberGenerator.GetBytes(32);
            await SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(_cached));
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
