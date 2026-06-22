using System.Security.Cryptography;

namespace FenrixCloudBudget.Services.Security;

/// <summary>
/// Development/server fallback key provider for hosts without MAUI SecureStorage
/// (unit tests, the ASP.NET Core API). Persists a 32-byte key to a protected file.
/// In production server mode, replace with a KMS/Key Vault-backed provider.
/// </summary>
public sealed class DevFileKeyProvider : ISecureKeyProvider
{
    private readonly string _path;
    private byte[]? _cached;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DevFileKeyProvider(string? path = null)
        => _path = path ?? Path.Combine(AppContext.BaseDirectory, ".fenrix.key");

    public async Task<byte[]> GetOrCreateKeyAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null) return _cached;
            if (File.Exists(_path))
            {
                _cached = Convert.FromBase64String(await File.ReadAllTextAsync(_path, ct));
            }
            else
            {
                _cached = RandomNumberGenerator.GetBytes(32);
                await File.WriteAllTextAsync(_path, Convert.ToBase64String(_cached), ct);
            }
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
