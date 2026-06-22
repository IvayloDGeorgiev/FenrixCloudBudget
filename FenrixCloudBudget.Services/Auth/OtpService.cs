using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Email.Templates;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Auth;

/// <summary>
/// Passwordless 6-digit email OTP, shared by the hosted API and the local MAUI app so an
/// invited member can sign in without a password. Security controls (a 6-digit code is weak
/// without these): store only a HASH of the code, short expiry, attempt limit + lockout.
/// Email delivery goes through the shared <see cref="INotificationService"/> (whichever
/// adapter the host configured — transactional provider on the server, configured sender locally).
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
    public async Task<bool> RequestCodeAsync(string email, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var userCount = await db.Users.CountAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(item => item.Email == email, ct);

        // The very first verified account bootstraps the workspace as Admin. After that,
        // sign-in codes are only issued to known/explicitly invited users.
        if (userCount > 0 && (user is null || user.Status == UserStatus.Disabled))
            return false;

        var pendingInvite = await db.Invitations
            .Where(item => item.Email == email && item.Status == InvitationStatus.Pending)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync(ct);
        var role = user?.Role
                   ?? pendingInvite?.Role
                   ?? (userCount == 0 ? UserRole.Admin : UserRole.Member);
        var code = OneTimeCode.Create();

        await db.Invitations
            .Where(i => i.Email == email && i.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Expired), ct);

        db.Invitations.Add(new Invitation
        {
            Email = email,
            Role = role,
            CodeHash = OneTimeCode.Hash(code, email),
            ExpiresUtc = DateTimeOffset.UtcNow.Add(Ttl),
            Status = InvitationStatus.Pending
        });
        await db.SaveChangesAsync(ct);

        await _notifications.SendEmailAsync(email, EmailTemplateRenderer.Otp,
            new { Code = code, Minutes = (int)Ttl.TotalMinutes }, ct);
        return true;
    }

    /// <summary>Verify a submitted code. Enforces expiry, attempt limit, and single-use.</summary>
    public async Task<OtpResult> VerifyCodeAsync(string email, string code, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var invite = await db.Invitations
            .Where(i => i.Email == email && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.Id)
            .FirstOrDefaultAsync(ct);

        if (invite is null) return OtpResult.Invalid("No pending code. Request a new one.");
        if (invite.ExpiresUtc < DateTimeOffset.UtcNow)
        {
            invite.Status = InvitationStatus.Expired;
            await db.SaveChangesAsync(ct);
            return OtpResult.Invalid("Code expired.");
        }
        if (invite.AttemptCount >= MaxAttempts)
        {
            invite.Status = InvitationStatus.Revoked;
            await db.SaveChangesAsync(ct);
            return OtpResult.Invalid("Too many attempts. Request a new code.");
        }

        invite.AttemptCount++;

        if (!OneTimeCode.Matches(invite.CodeHash, code, email))
        {
            if (invite.AttemptCount >= MaxAttempts)
                invite.Status = InvitationStatus.Revoked;
            await db.SaveChangesAsync(ct);
            return OtpResult.Invalid("Incorrect code.");
        }

        invite.Status = InvitationStatus.Accepted;

        // Upsert the user as Active.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User
            {
                Email = email,
                Role = invite.Role,
                Status = UserStatus.Active,
                LastLoginUtc = DateTimeOffset.UtcNow
            };
            db.Users.Add(user);
        }
        else
        {
            if (user.Status == UserStatus.Disabled)
                return OtpResult.Invalid("This account is disabled.");

            user.Role = invite.Role;
            user.Status = UserStatus.Active;
            user.LastLoginUtc = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return OtpResult.Ok(user.Id, user.Email, user.Role);
    }
}

public record OtpResult(
    bool Success,
    int UserId = 0,
    string? Email = null,
    UserRole Role = UserRole.Member,
    string? Error = null)
{
    public static OtpResult Ok(int userId, string email, UserRole role) => new(true, userId, email, role);
    public static OtpResult Invalid(string error) => new(false, Error: error);
}
