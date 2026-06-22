using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Security;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Services.Auth;

public sealed class LocalPasswordAuthenticationService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;

    public LocalPasswordAuthenticationService(IDbContextFactory<AppDbContext> dbf)
        => _dbf = dbf;

    public async Task<LocalLoginResult> LoginAsync(
        string identifier,
        string password,
        CancellationToken ct = default)
    {
        if (!SeededAdminAccount.MatchesIdentifier(identifier)
            || !SeededAdminAccount.VerifyPassword(password))
            return LocalLoginResult.Fail("Incorrect username or password.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(
            item => item.Email == SeededAdminAccount.Email,
            ct);

        if (user is null)
            return LocalLoginResult.Fail("The seeded administrator has not been created yet.");
        if (user.Status == UserStatus.Disabled)
            return LocalLoginResult.Fail("The seeded administrator account is disabled.");

        user.Role = UserRole.Admin;
        user.Status = UserStatus.Active;
        user.LastLoginUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return LocalLoginResult.Ok(user);
    }
}

public sealed record LocalLoginResult(bool Success, User? User = null, string? Error = null)
{
    public static LocalLoginResult Ok(User user) => new(true, user);
    public static LocalLoginResult Fail(string error) => new(false, Error: error);
}
