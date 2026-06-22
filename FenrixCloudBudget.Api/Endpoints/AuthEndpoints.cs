using FenrixCloudBudget.Api.Services;

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

        group.MapPost("/verify", async (OtpVerify body, OtpService otp, CancellationToken ct) =>
        {
            var result = await otp.VerifyCodeAsync(
                (body.Email ?? "").Trim().ToLowerInvariant(), body.Code ?? "", ct);

            return result.Success
                ? Results.Ok(new { result.Email, Role = result.Role.ToString() /* TODO: + session token */ })
                : Results.BadRequest(new { error = result.Error });
        });
    }
}

public record OtpRequest(string Email);
public record OtpVerify(string Email, string Code);
