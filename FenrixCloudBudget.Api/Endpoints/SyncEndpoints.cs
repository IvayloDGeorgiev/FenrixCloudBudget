using FenrixCloudBudget.Core.Models;
using FenrixCloudBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Api.Endpoints;

/// <summary>
/// Authenticated SaaS snapshot endpoints. This first contract is intentionally pull-only:
/// credentials never leave a device, while stable cross-device IDs and tenant ownership are
/// added before write/conflict endpoints.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Sync")
            .RequireAuthorization();

        group.MapGet("/sync/snapshot", async (
            IDbContextFactory<AppDbContext> dbf,
            TimeProvider time,
            CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);

            var clients = await db.Clients.AsNoTracking()
                .Select(item => new SaasClientDto(
                    item.Id,
                    item.Name,
                    item.CompanyName,
                    item.ContactName,
                    item.ContactEmail,
                    item.ContactPhone,
                    item.BillingAddress,
                    item.BillingEmail,
                    item.Notes,
                    item.Tags,
                    item.CreatedUtc,
                    item.UpdatedUtc))
                .ToListAsync(ct);

            var projects = await db.Projects.AsNoTracking()
                .Select(item => new SaasProjectDto(
                    item.Id,
                    item.Name,
                    item.Description,
                    item.Status,
                    item.Currency,
                    item.ClientId,
                    item.CreatedUtc,
                    item.UpdatedUtc))
                .ToListAsync(ct);

            var services = await db.Services.AsNoTracking()
                .Select(item => new SaasServiceDto(
                    item.Id,
                    item.ProjectId,
                    item.Name,
                    item.Provider,
                    item.ServiceType,
                    item.Tier,
                    item.Source,
                    item.ExternalResourceId,
                    item.EstimatedCost,
                    item.EstimatePeriod,
                    item.Currency,
                    item.CreatedUtc,
                    item.UpdatedUtc))
                .ToListAsync(ct);

            var budgets = await db.Budgets.AsNoTracking()
                .Select(item => new SaasBudgetDto(
                    item.Id,
                    item.ProjectId,
                    item.ServiceId,
                    item.Name,
                    item.Period,
                    item.Amount,
                    item.Currency,
                    item.Thresholds,
                    item.IsEnabled,
                    item.CreatedUtc,
                    item.UpdatedUtc))
                .ToListAsync(ct);

            var reminders = await db.Reminders.AsNoTracking()
                .Select(item => new SaasReminderDto(
                    item.Id,
                    item.Title,
                    item.Notes,
                    item.Type,
                    item.Status,
                    item.CustomTarget,
                    item.DueDate,
                    item.LeadTimesDays,
                    item.Channels,
                    item.RecurrenceDays,
                    item.SnoozedUntilUtc,
                    item.LastFiredUtc,
                    item.CreatedUtc,
                    item.UpdatedUtc))
                .ToListAsync(ct);

            var costRecords = await db.CostRecords.AsNoTracking()
                .Select(item => new SaasCostRecordDto(
                    item.Id,
                    item.ServiceId,
                    item.Date,
                    item.Amount,
                    item.Currency,
                    item.Source,
                    item.SyncedUtc,
                    item.CreatedUtc,
                    item.UpdatedUtc))
                .ToListAsync(ct);

            return Results.Ok(new SaasWorkspaceSnapshot(
                time.GetUtcNow(),
                clients,
                projects,
                services,
                budgets,
                reminders,
                costRecords));
        });

        // Preserve the original convenience reads while clients migrate to the snapshot contract.
        group.MapGet("/projects", async (IDbContextFactory<AppDbContext> dbf, CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            return Results.Ok(await db.Projects.AsNoTracking().ToListAsync(ct));
        });

        group.MapGet("/clients", async (IDbContextFactory<AppDbContext> dbf, CancellationToken ct) =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            return Results.Ok(await db.Clients.AsNoTracking().ToListAsync(ct));
        });
    }
}
