using FenrixCloudBudget.Api.Services;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Security;
using FenrixCloudBudget.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class Phase5AuthTests
{
    [Fact]
    public async Task FirstOtpUser_BootstrapsAdmin_AndUnknownEmailIsRejectedAfterwards()
    {
        using var test = new TestDb();
        var notifications = new CapturingNotificationService();
        var otp = new OtpService(test.Factory, notifications);

        Assert.True(await otp.RequestCodeAsync("owner@example.com"));
        var ownerCode = notifications.LastCode();
        var verified = await otp.VerifyCodeAsync("owner@example.com", ownerCode);

        Assert.True(verified.Success);
        Assert.Equal(UserRole.Admin, verified.Role);
        Assert.True(verified.UserId > 0);

        Assert.False(await otp.RequestCodeAsync("stranger@example.com"));
        await using var db = test.NewContext();
        Assert.False(await db.Users.AnyAsync(user => user.Email == "stranger@example.com"));
        Assert.False(await db.Invitations.AnyAsync(invite => invite.Email == "stranger@example.com"));
    }

    [Fact]
    public async Task Invitation_CreatesInvitedUser_AndCodeActivatesAssignedRole()
    {
        using var test = new TestDb();
        var notifications = new CapturingNotificationService();
        var invitations = new UserInvitationService(test.Factory, notifications);

        var issued = await invitations.InviteAsync("member@example.com", UserRole.Member, "Acme");

        Assert.True(issued.Success);
        Assert.True(issued.EmailDelivered);
        var code = notifications.LastCode();

        var otp = new OtpService(test.Factory, notifications);
        var verified = await otp.VerifyCodeAsync("member@example.com", code);

        Assert.True(verified.Success);
        Assert.Equal(UserRole.Member, verified.Role);
        await using var db = test.NewContext();
        var user = await db.Users.SingleAsync(item => item.Email == "member@example.com");
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotNull(user.LastLoginUtc);
    }

    [Fact]
    public void Jwt_ContainsSessionIdentityAndRole()
    {
        var service = new JwtTokenService(
            new JwtOptions
            {
                SigningKey = "test-only-FenrixCloudBudget-signing-key-that-is-long-enough-2026"
            },
            TimeProvider.System);

        var session = service.Issue(42, "admin@example.com", UserRole.Admin);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(session.AccessToken);

        Assert.Contains(token.Claims, claim => claim.Type == System.Security.Claims.ClaimTypes.NameIdentifier && claim.Value == "42");
        Assert.Contains(token.Claims, claim => claim.Type == System.Security.Claims.ClaimTypes.Role && claim.Value == "Admin");
        Assert.True(session.ExpiresUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SeededAdministrator_UsesBootstrapCredential_AndCannotBeInvited()
    {
        Assert.True(SeededAdminAccount.MatchesIdentifier("admin"));
        Assert.True(SeededAdminAccount.MatchesIdentifier("ADMIN@FENRIX.LOCAL"));
        Assert.True(SeededAdminAccount.VerifyPassword("123456"));
        Assert.False(SeededAdminAccount.VerifyPassword("incorrect"));

        using var test = new TestDb();
        var invitations = new UserInvitationService(
            test.Factory,
            new CapturingNotificationService());

        var result = await invitations.InviteAsync(
            SeededAdminAccount.Email,
            UserRole.Member);

        Assert.False(result.Success);
    }

    private sealed class CapturingNotificationService : INotificationService
    {
        private readonly List<object> _emailData = [];

        public string LastCode()
        {
            var data = _emailData.Last();
            return data.GetType().GetProperty("Code")?.GetValue(data)?.ToString()
                   ?? throw new InvalidOperationException("No OTP code was captured.");
        }

        public Task NotifyInAppAsync(string title, string message, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyLocalAsync(string title, string message, DateTimeOffset? at = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> SendEmailAsync(string to, string templateKey, object data, CancellationToken ct = default)
        {
            _emailData.Add(data);
            return Task.FromResult(true);
        }

        public Task DispatchAsync(NotificationRequest request, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
