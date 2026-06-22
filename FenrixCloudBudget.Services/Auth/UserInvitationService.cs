using System.Net.Mail;
using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Security;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Email.Templates;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Auth;

/// <summary>
/// Creates, sends, resends, and revokes workspace invitations. The invitation code is
/// persisted only as a hash and doubles as the invited user's first passwordless sign-in.
/// </summary>
public sealed class UserInvitationService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly INotificationService _notifications;

    public UserInvitationService(
        IDbContextFactory<AppDbContext> dbf,
        INotificationService notifications)
    {
        _dbf = dbf;
        _notifications = notifications;
    }

    public async Task<InvitationIssueResult> InviteAsync(
        string email,
        UserRole role,
        string workspaceName = "your Fenrix workspace",
        CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out _))
            return InvitationIssueResult.Fail("Enter a valid email address.");
        if (string.Equals(email, SeededAdminAccount.Email, StringComparison.OrdinalIgnoreCase))
            return InvitationIssueResult.Fail("The seeded administrator is managed directly from the Users page.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(item => item.Email == email, ct);
        if (user?.Status == UserStatus.Active)
            return InvitationIssueResult.Fail("That user is already active.");

        await db.Invitations
            .Where(item => item.Email == email && item.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.Status, InvitationStatus.Expired),
                ct);

        var code = OneTimeCode.Create();
        var invitation = new Invitation
        {
            Email = email,
            Role = role,
            CodeHash = OneTimeCode.Hash(code, email),
            ExpiresUtc = DateTimeOffset.UtcNow.Add(Ttl),
            Status = InvitationStatus.Pending
        };
        db.Invitations.Add(invitation);

        if (user is null)
        {
            user = new User
            {
                Email = email,
                Role = role,
                Status = UserStatus.Invited
            };
            db.Users.Add(user);
        }
        else
        {
            user.Role = role;
            user.Status = UserStatus.Invited;
        }

        await db.SaveChangesAsync(ct);

        var delivered = await _notifications.SendEmailAsync(
            email,
            EmailTemplateRenderer.Invitation,
            new
            {
                OrgName = workspaceName,
                Code = code,
                Hours = (int)Ttl.TotalHours
            },
            ct);

        return InvitationIssueResult.Ok(invitation.Id, user.Id, delivered);
    }

    public async Task<bool> RevokeAsync(int invitationId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var invitation = await db.Invitations.FirstOrDefaultAsync(item => item.Id == invitationId, ct);
        if (invitation is null || invitation.Status != InvitationStatus.Pending)
            return false;

        invitation.Status = InvitationStatus.Revoked;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record InvitationIssueResult(
    bool Success,
    int InvitationId = 0,
    int UserId = 0,
    bool EmailDelivered = false,
    string? Error = null)
{
    public static InvitationIssueResult Ok(int invitationId, int userId, bool delivered)
        => new(true, invitationId, userId, delivered);

    public static InvitationIssueResult Fail(string error)
        => new(false, Error: error);
}
