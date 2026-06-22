using FenrixCloudBudget.Api.Services;
using FenrixCloudBudget.Services.Auth;
using Microsoft.AspNetCore.Authorization;

namespace FenrixCloudBudget.Api.Endpoints;

/// <summary>
/// Passwordless auth endpoints (Phase 5). A simple per-IP/email rate limiter should be
/// attached in front of /request (configured in Program.cs via AddRateLimiter).
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/request", async (OtpRequest body, OtpService otp, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email))
                return Results.BadRequest(new { error = "Email is required." });

            await otp.RequestCodeAsync(body.Email.Trim().ToLowerInvariant(), ct);
            // Always return 200 to avoid leaking which emails exist.
            return Results.Ok(new { message = "If the address is valid, a code has been sent." });
        })
        .RequireRateLimiting("otp");

        group.MapPost("/verify", async (
            OtpVerify body,
            OtpService otp,
            JwtTokenService tokens,
            CancellationToken ct) =>
        {
            var result = await otp.VerifyCodeAsync(
                (body.Email ?? "").Trim().ToLowerInvariant(), body.Code ?? "", ct);

            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            var session = tokens.Issue(result.UserId, result.Email!, result.Role);
            return Results.Ok(new
            {
                result.UserId,
                result.Email,
                Role = result.Role.ToString(),
                session.AccessToken,
                session.ExpiresUtc,
                session.TokenType
            });
        });

        group.MapPost("/password", async (
            PasswordLogin body,
            LocalPasswordAuthenticationService passwords,
            JwtTokenService tokens,
            CancellationToken ct) =>
        {
            var result = await passwords.LoginAsync(
                body.Identifier ?? string.Empty,
                body.Password ?? string.Empty,
                ct);
            if (!result.Success || result.User is null)
                return Results.BadRequest(new { error = result.Error });

            var session = tokens.Issue(
                result.User.Id,
                result.User.Email,
                result.User.Role);
            return Results.Ok(new
            {
                UserId = result.User.Id,
                result.User.Email,
                Role = result.User.Role.ToString(),
                session.AccessToken,
                session.ExpiresUtc,
                session.TokenType
            });
        })
        .RequireRateLimiting("otp");

        group.MapGet("/me", [Authorize] (System.Security.Claims.ClaimsPrincipal principal) =>
            Results.Ok(new
            {
                UserId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                Email = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("email")?.Value,
                Role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            }));
    }
}

public record OtpRequest(string Email);
public record OtpVerify(string Email, string Code);
public record PasswordLogin(string? Identifier, string? Password);
