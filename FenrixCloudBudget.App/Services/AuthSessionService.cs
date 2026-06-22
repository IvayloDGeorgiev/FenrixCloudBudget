using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Security;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.App.Services;

/// <summary>
/// In-memory local desktop/mobile session. Two sign-in paths:
///   • Password — the seeded bootstrap administrator (offline, no email needed).
///   • Email code (OTP) — any invited member; the same passwordless flow the hosted API uses,
///     delivered through whichever email adapter is configured in Settings.
/// Local mode intentionally requires sign-in after each launch.
/// </summary>
public sealed class AuthSessionService
{
    private readonly LocalPasswordAuthenticationService _passwords;
    private readonly OtpService _otp;
    private readonly IDbContextFactory<AppDbContext> _dbf;

    public AuthSessionService(
        LocalPasswordAuthenticationService passwords,
        OtpService otp,
        IDbContextFactory<AppDbContext> dbf)
    {
        _passwords = passwords;
        _otp = otp;
        _dbf = dbf;
    }

    public User? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin
                           && CurrentUser.Status == UserStatus.Active;

    public event Action? Changed;

    public async Task<LocalLoginResult> LoginAsync(
        string identifier,
        string password,
        CancellationToken ct = default)
    {
        var result = await _passwords.LoginAsync(identifier, password, ct);
        if (!result.Success)
            return result;

        CurrentUser = result.User;
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// Send a one-time sign-in code to an invited member's email. Returns false when the address
    /// isn't a known/invited user — but callers should show the same neutral message either way
    /// so the UI never reveals which addresses exist.
    /// </summary>
    public Task<bool> RequestSignInCodeAsync(string email, CancellationToken ct = default)
        => _otp.RequestCodeAsync(NormalizeEmail(email), ct);

    /// <summary>Verify an emailed code and, on success, start the session for that member.</summary>
    public async Task<LocalLoginResult> LoginWithCodeAsync(
        string email,
        string code,
        CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        var result = await _otp.VerifyCodeAsync(normalized, (code ?? string.Empty).Trim(), ct);
        if (!result.Success)
            return LocalLoginResult.Fail(result.Error ?? "Sign-in failed.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == result.UserId, ct);
        if (user is null)
            return LocalLoginResult.Fail("Your account could not be loaded after verification.");

        CurrentUser = user;
        Changed?.Invoke();
        return LocalLoginResult.Ok(user);
    }

    /// <summary>
    /// Password sign-in only authenticates the seeded bootstrap administrator. If that account
    /// has been disabled, the password path is dead weight — the UI should hide it (and the
    /// bootstrap hint) and offer email-code sign-in only.
    /// </summary>
    public async Task<bool> IsPasswordSignInAvailableAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == SeededAdminAccount.Email, ct);
        return admin is not null && admin.Status != UserStatus.Disabled;
    }

    public void Logout()
    {
        CurrentUser = null;
        Changed?.Invoke();
    }

    private static string NormalizeEmail(string email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();
}
