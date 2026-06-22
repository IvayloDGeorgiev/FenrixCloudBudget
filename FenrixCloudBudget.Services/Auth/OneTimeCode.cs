using System.Security.Cryptography;
using System.Text;

namespace FenrixCloudBudget.Services.Auth;

/// <summary>Shared one-time-code generation and constant-time hash verification.</summary>
public static class OneTimeCode
{
    public static string Create()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(string code, string salt)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));

    public static bool Matches(string expectedHash, string code, string salt)
    {
        var expected = Encoding.UTF8.GetBytes(expectedHash);
        var actual = Encoding.UTF8.GetBytes(Hash(code, salt));
        return expected.Length == actual.Length
               && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
