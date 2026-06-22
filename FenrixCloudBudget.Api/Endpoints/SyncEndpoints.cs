using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Api.Endpoints;

/// <summary>
/// SaaS data-sync endpoints (Phase 5). The desktop/mobile app in SaaS mode treats the API as
/// the source of truth and keeps a local SQLite cache. These are read-only skeletons; add
/// auth (session token from /auth/verify), tenant scoping, and write/merge endpoints next.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Sync").RequireAuthorization();

        group.MapGet("/projects", async (IDbContextFactory<AppDbContext> dbf, CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var projects = await db.Projects
                .Include(p => p.Client)
                .Include(p => p.Services)
                .Include(p => p.Budgets)
                .AsNoTracking()
                .ToListAsync(ct);
            return Results.Ok(projects);
        });

        group.MapGet("/clients", async (IDbContextFactory<AppDbContext> dbf, CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            return Results.Ok(await db.Clients.AsNoTracking().ToListAsync(ct));
        });

        // TODO(Phase 5): POST /api/sync (push local changes), GET /api/changes?since= (pull deltas).
    }
}
