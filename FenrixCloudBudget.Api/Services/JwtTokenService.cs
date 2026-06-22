using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FenrixCloudBudget.Core.Enums;
using Microsoft.IdentityModel.Tokens;

namespace FenrixCloudBudget.Api.Services;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "FenrixCloudBudget.Api";
    public string Audience { get; set; } = "FenrixCloudBudget.Client";
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 480;

    public SymmetricSecurityKey SecurityKey()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException("Authentication:Jwt:SigningKey must be at least 32 bytes.");

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
    }
}

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public JwtTokenService(JwtOptions options, TimeProvider time)
    {
        _options = options;
        _time = time;
    }

    public SessionToken Issue(int userId, string email, UserRole role)
    {
        var now = _time.GetUtcNow();
        var expires = now.AddMinutes(Math.Clamp(_options.LifetimeMinutes, 5, 24 * 60));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(_options.SecurityKey(), SecurityAlgorithms.HmacSha256));

        return new SessionToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            "Bearer");
    }
}

public sealed record SessionToken(string AccessToken, DateTimeOffset ExpiresUtc, string TokenType);
