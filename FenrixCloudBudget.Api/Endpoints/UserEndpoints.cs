using System.Security.Claims;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Security;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Users")
            .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Admin)));

        group.MapGet("/users", async (IDbContextFactory<AppDbContext> dbf, CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var users = await db.Users
                .AsNoTracking()
                .OrderBy(item => item.Email)
                .Select(item => new
                {
                    item.Id,
                    item.Email,
                    item.DisplayName,
                    Role = item.Role.ToString(),
                    Status = item.Status.ToString(),
                    item.LastLoginUtc
                })
                .ToListAsync(ct);
            return Results.Ok(users);
        });

        group.MapGet("/invitations", async (IDbContextFactory<AppDbContext> dbf, CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var invitations = await db.Invitations
                .AsNoTracking()
                .Where(item => item.Status == InvitationStatus.Pending)
                .OrderByDescending(item => item.Id)
                .Select(item => new
                {
                    item.Id,
                    item.Email,
                    Role = item.Role.ToString(),
                    Status = item.Status.ToString(),
                    item.ExpiresUtc,
                    item.CreatedUtc
                })
                .ToListAsync(ct);
            return Results.Ok(invitations);
        });

        group.MapPost("/invitations", async (
            InviteUserRequest body,
            UserInvitationService invitations,
            CancellationToken ct) =>
        {
            var result = await invitations.InviteAsync(
                body.Email ?? string.Empty,
                body.Role,
                string.IsNullOrWhiteSpace(body.WorkspaceName) ? "your Fenrix workspace" : body.WorkspaceName.Trim(),
                ct);

            return result.Success
                ? Results.Created($"/api/invitations/{result.InvitationId}", new
                {
                    result.InvitationId,
                    result.UserId,
                    result.EmailDelivered
                })
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapDelete("/invitations/{id:int}", async (
            int id,
            UserInvitationService invitations,
            CancellationToken ct) =>
            await invitations.RevokeAsync(id, ct)
                ? Results.NoContent()
                : Results.NotFound());

        group.MapPatch("/users/{id:int}", async (
            int id,
            UpdateUserRequest body,
            ClaimsPrincipal principal,
            IDbContextFactory<AppDbContext> dbf,
            CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id, ct);
            if (user is null)
                return Results.NotFound();

            var requesterId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (requesterId == id.ToString())
            {
                if (body.Status == UserStatus.Disabled)
                    return Results.BadRequest(new { error = "You cannot disable your own session." });
                if (body.Role != user.Role)
                    return Results.BadRequest(new { error = "You cannot change your own role." });
            }

            if (string.Equals(user.Email, SeededAdminAccount.Email, StringComparison.OrdinalIgnoreCase)
                && body.Role != UserRole.Admin)
                return Results.BadRequest(new { error = "The seeded administrator role cannot be changed." });

            if (user.Role == UserRole.Admin
                && (body.Role != UserRole.Admin || body.Status == UserStatus.Disabled))
            {
                var activeAdmins = await db.Users.CountAsync(
                    item => item.Role == UserRole.Admin && item.Status == UserStatus.Active,
                    ct);
                if (activeAdmins <= 1)
                    return Results.BadRequest(new { error = "The workspace must retain at least one active admin." });
            }

            user.Role = body.Role;
            user.Status = body.Status;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapDelete("/users/{id:int}", async (
            int id,
            ClaimsPrincipal principal,
            IDbContextFactory<AppDbContext> dbf,
            CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id, ct);
            if (user is null)
                return Results.NotFound();

            if (principal.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString())
                return Results.BadRequest(new { error = "You cannot delete your current account." });

            if (string.Equals(user.Email, SeededAdminAccount.Email, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "The seeded administrator can be disabled, but not deleted." });

            if (user.Role == UserRole.Admin)
            {
                var activeAdmins = await db.Users.CountAsync(
                    item => item.Role == UserRole.Admin && item.Status == UserStatus.Active,
                    ct);
                if (activeAdmins <= 1)
                    return Results.BadRequest(new { error = "The workspace must retain at least one active admin." });
            }

            await db.Invitations
                .Where(invitation => invitation.Email == user.Email)
                .ExecuteDeleteAsync(ct);
            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}

public sealed record InviteUserRequest(string? Email, UserRole Role, string? WorkspaceName = null);
public sealed record UpdateUserRequest(UserRole Role, UserStatus Status);
