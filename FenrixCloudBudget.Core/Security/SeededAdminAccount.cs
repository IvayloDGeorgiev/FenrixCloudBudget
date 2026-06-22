using System.Security.Cryptography;
using System.Text;

namespace FenrixCloudBudget.Core.Security;

/// <summary>
/// Development bootstrap identity. The default password is never persisted in the database.
/// Replace/remove this credential before production distribution.
/// </summary>
public static class SeededAdminAccount
{
    public const string Email = "admin@fenrix.local";
    public const string LoginAlias = "admin";
    public const string DisplayName = "Fenrix Administrator";

    private const string DefaultPassword = "123456";
    private static readonly byte[] ExpectedPasswordHash =
        SHA256.HashData(Encoding.UTF8.GetBytes(DefaultPassword));

    public static bool MatchesIdentifier(string? value)
        => string.Equals(value?.Trim(), LoginAlias, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value?.Trim(), Email, StringComparison.OrdinalIgnoreCase);

    public static bool VerifyPassword(string? password)
    {
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(ExpectedPasswordHash, supplied);
    }
}
