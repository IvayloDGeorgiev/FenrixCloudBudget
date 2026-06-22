using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data;

/// <summary>
/// An AES-encrypted secret blob (cloud client secrets, email API keys, connection strings).
/// Written by the ISecretStore implementation. Entities elsewhere only store the opaque
/// <see cref="Reference"/> plus a masked hint — never the secret itself.
/// </summary>
public class SecretEntry
{
    public int Id { get; set; }

    /// <summary>Opaque GUID reference stored on owning entities.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Base64 of (nonce | tag | ciphertext) from AES-256-GCM.</summary>
    public string Blob { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class SecretEntryConfig : IEntityTypeConfiguration<SecretEntry>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<SecretEntry> b)
    {
        b.Property(x => x.Reference).IsRequired().HasMaxLength(64);
        b.HasIndex(x => x.Reference).IsUnique();
    }
}
