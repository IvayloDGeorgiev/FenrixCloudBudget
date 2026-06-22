using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Services.Auth;

namespace FenrixCloudBudget.App.Services;

/// <summary>
/// In-memory local desktop/mobile session. Hosted SaaS mode exchanges the same seeded
/// credential or OTP for a JWT; local mode intentionally requires sign-in after each launch.
/// </summary>
public sealed class AuthSessionService
{
    private readonly LocalPasswordAuthenticationService _passwords;

    public AuthSessionService(LocalPasswordAuthenticationService passwords)
        => _passwords = passwords;

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

    public void Logout()
    {
        CurrentUser = null;
        Changed?.Invoke();
    }
}
