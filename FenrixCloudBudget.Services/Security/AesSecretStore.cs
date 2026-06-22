using System.Security.Cryptography;
using System.Text;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Security;

/// <summary>
/// ISecretStore backed by AES-256-GCM. The master key comes from ISecureKeyProvider
/// (OS secure storage). Encrypted blobs are kept in the SecretEntries table keyed by an
/// opaque reference; entities only ever store that reference + a masked hint.
/// </summary>
public sealed class AesSecretStore : ISecretStore
{
    private readonly ISecureKeyProvider _keys;
    private readonly IDbContextFactory<AppDbContext> _dbf;

    public AesSecretStore(ISecureKeyProvider keys, IDbContextFactory<AppDbContext> dbf)
    {
        _keys = keys;
        _dbf = dbf;
    }

    public async Task<SecretHandle> SaveAsync(string secret, CancellationToken ct = default)
    {
        var key = await _keys.GetOrCreateKeyAsync(ct);
        var reference = Guid.NewGuid().ToString("N");

        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = Convert.ToBase64String(Concat(nonce, tag, ciphertext));

        await using var db = await _dbf.CreateDbContextAsync(ct);
        db.SecretEntries.Add(new SecretEntry { Reference = reference, Blob = blob });
        await db.SaveChangesAsync(ct);

        return new SecretHandle(reference, ISecretStore.Mask(secret));
    }

    public async Task<string?> GetAsync(string reference, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var entry = await db.SecretEntries.FirstOrDefaultAsync(x => x.Reference == reference, ct);
        if (entry is null) return null;

        var key = await _keys.GetOrCreateKeyAsync(ct);
        var all = Convert.FromBase64String(entry.Blob);
        var n = AesGcm.NonceByteSizes.MaxSize;
        var t = AesGcm.TagByteSizes.MaxSize;
        var nonce = all[..n];
        var tag = all[n..(n + t)];
        var ciphertext = all[(n + t)..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, t);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    public async Task RemoveAsync(string reference, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        await db.SecretEntries.Where(x => x.Reference == reference).ExecuteDeleteAsync(ct);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }
}
