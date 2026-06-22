using System.Security.Cryptography;
using System.Text;
using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Email.Templates;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Api.Services;

/// <summary>
/// Passwordless 6-digit email OTP. Security controls (a 6-digit code is weak without these):
/// store only a HASH of the code, short expiry, rate limit + lockout after N attempts.
/// Email delivery goes through the shared INotificationService (server-side transactional provider).
/// </summary>
public sealed class OtpService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly INotificationService _notifications;

    public OtpService(IDbContextFactory<AppDbContext> dbf, INotificationService notifications)
    {
        _dbf = dbf;
        _notifications = notifications;
    }

    /// <summary>Issue (or re-issue) a code for an email and send it. Returns nothing sensitive.</summary>
    public async Task RequestCodeAsync(string email, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        await using var db = await _dbf.CreateDbContextAsync(ct);

        // Invalidate any prior pending invitations for this email.
        await db.Invitations
            .Where(i => i.Email == email && i.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Expired), ct);

        db.Invitations.Add(new Invitation
        {
            Email = email,
            CodeHash = Hash(code, email),
            ExpiresUtc = DateTimeOffset.UtcNow.Add(Ttl),
            Status = InvitationStatus.Pending
        });
        await db.SaveChangesAsync(ct);

        await _notifications.SendEmailAsync(email, EmailTemplateRenderer.Otp,
            new { Code = code, Minutes = (int)Ttl.TotalMinutes }, ct);
    }

    /// <summary>Verify a submitted code. Enforces expiry, attempt limit, and single-use.</summary>
    public async Task<OtpResult> VerifyCodeAsync(string email, string code, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var invite = await db.Invitations
            .Where(i => i.Email == email && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedUtc)
            .FirstOrDefaultAsync(ct);

        if (invite is null) return OtpResult.Invalid("No pending code. Request a new one.");
        if (invite.ExpiresUtc < DateTimeOffset.UtcNow) return OtpResult.Invalid("Code expired.");
        if (invite.AttemptCount >= MaxAttempts)
        {
            invite.Status = InvitationStatus.Revoked;
            await db.SaveChangesAsync(ct);
            return OtpResult.Invalid("Too many attempts. Request a new code.");
        }

        invite.AttemptCount++;

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(invite.CodeHash), Encoding.UTF8.GetBytes(Hash(code, email))))
        {
            await db.SaveChangesAsync(ct);
            return OtpResult.Invalid("Incorrect code.");
        }

        invite.Status = InvitationStatus.Accepted;

        // Upsert the user as Active.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User { Email = email, Role = invite.Role, Status = UserStatus.Active };
            db.Users.Add(user);
        }
        else
        {
            user.Status = UserStatus.Active;
            user.LastLoginUtc = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        // TODO: issue a signed session token (JWT) here and return it.
        return OtpResult.Ok(user.Email, user.Role);
    }

    private static string Hash(string code, string salt)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));
}

public record OtpResult(bool Success, string? Email = null, UserRole Role = UserRole.Member, string? Error = null)
{
    public static OtpResult Ok(string email, UserRole role) => new(true, email, role);
    public static OtpResult Invalid(string error) => new(false, Error: error);
}
