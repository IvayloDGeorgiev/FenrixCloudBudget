using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data.Providers;

/// <summary>
/// SaaS mode: the hosted API is the source of truth and local SQLite is an offline read cache.
/// The current contract hydrates a credential-free snapshot over an authenticated connection.
/// </summary>
public sealed class SaaSDataProvider : IDataProvider
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly HttpClient _http;
    private readonly string? _accessToken;

    public DataProviderMode Mode => DataProviderMode.Saas;

    public SaaSDataProvider(
        IDbContextFactory<AppDbContext> factory,
        string baseUrl,
        string? accessToken,
        HttpClient? httpClient = null)
    {
        _factory = factory;
        _accessToken = accessToken;
        _http = httpClient ?? new HttpClient();

        if (Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var address))
            _http.BaseAddress = address;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using (var db = await _factory.CreateDbContextAsync(ct))
            await db.Database.MigrateAsync(ct);

        if (_http.BaseAddress is not null && !string.IsNullOrWhiteSpace(_accessToken))
            await SyncAsync(ct);
    }

    public async Task<DataProviderTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        if (_http.BaseAddress is null)
            return new DataProviderTestResult(false, "A valid SaaS API URL is required.");
        if (string.IsNullOrWhiteSpace(_accessToken))
            return new DataProviderTestResult(false, "Sign in before testing the SaaS connection.");

        var timer = Stopwatch.StartNew();
        try
        {
            using var request = Authorized(HttpMethod.Get, "auth/me");
            using var response = await _http.SendAsync(request, ct);
            timer.Stop();
            return response.IsSuccessStatusCode
                ? new DataProviderTestResult(true, "Authenticated", timer.Elapsed)
                : new DataProviderTestResult(false, $"Authentication failed (HTTP {(int)response.StatusCode}).", timer.Elapsed);
        }
        catch (Exception ex)
        {
            timer.Stop();
            return new DataProviderTestResult(false, ex.Message, timer.Elapsed);
        }
    }

    public async Task<SaasSyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (_http.BaseAddress is null)
            return SaasSyncResult.Fail("A valid SaaS API URL is required.");
        if (string.IsNullOrWhiteSpace(_accessToken))
            return SaasSyncResult.Fail("A session token is required.");

        using var request = Authorized(HttpMethod.Get, "api/sync/snapshot");
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return SaasSyncResult.Fail($"Snapshot request failed with HTTP {(int)response.StatusCode}.");

        var snapshot = await response.Content.ReadFromJsonAsync<SaasWorkspaceSnapshot>(cancellationToken: ct);
        if (snapshot is null)
            return SaasSyncResult.Fail("The API returned an empty snapshot.");

        await ReplaceCacheAsync(snapshot, ct);
        return SaasSyncResult.Ok(
            snapshot.ServerUtc,
            snapshot.Clients.Count
            + snapshot.Projects.Count
            + snapshot.Services.Count
            + snapshot.Budgets.Count
            + snapshot.Reminders.Count
            + snapshot.CostRecords.Count);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    private async Task ReplaceCacheAsync(SaasWorkspaceSnapshot snapshot, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Provider credentials remain device-local. Preserve existing account links where
        // this cache has already seen the same server-side service/reminder.
        var localServiceAccounts = await db.Services
            .Where(item => item.CloudAccountId != null)
            .ToDictionaryAsync(item => item.Id, item => item.CloudAccountId, ct);
        var localReminderAccounts = await db.Reminders
            .Where(item => item.CloudAccountId != null)
            .ToDictionaryAsync(item => item.Id, item => item.CloudAccountId, ct);

        await db.CostRecords.ExecuteDeleteAsync(ct);
        await db.Budgets.ExecuteDeleteAsync(ct);
        await db.Services.ExecuteDeleteAsync(ct);
        await db.Projects.ExecuteDeleteAsync(ct);
        await db.Clients.ExecuteDeleteAsync(ct);
        await db.Reminders.ExecuteDeleteAsync(ct);

        db.Clients.AddRange(snapshot.Clients.Select(item => new Client
        {
            Id = item.Id,
            Name = item.Name,
            CompanyName = item.CompanyName,
            ContactName = item.ContactName,
            ContactEmail = item.ContactEmail,
            ContactPhone = item.ContactPhone,
            BillingAddress = item.BillingAddress,
            BillingEmail = item.BillingEmail,
            Notes = item.Notes,
            Tags = item.Tags,
            CreatedUtc = item.CreatedUtc,
            UpdatedUtc = item.UpdatedUtc
        }));

        db.Projects.AddRange(snapshot.Projects.Select(item => new Project
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            Status = item.Status,
            Currency = item.Currency,
            ClientId = item.ClientId,
            CreatedUtc = item.CreatedUtc,
            UpdatedUtc = item.UpdatedUtc
        }));

        db.Services.AddRange(snapshot.Services.Select(item => new Service
        {
            Id = item.Id,
            ProjectId = item.ProjectId,
            Name = item.Name,
            Provider = item.Provider,
            ServiceType = item.ServiceType,
            Tier = item.Tier,
            Source = item.Source,
            ExternalResourceId = item.ExternalResourceId,
            CloudAccountId = localServiceAccounts.GetValueOrDefault(item.Id),
            EstimatedCost = item.EstimatedCost,
            EstimatePeriod = item.EstimatePeriod,
            Currency = item.Currency,
            CreatedUtc = item.CreatedUtc,
            UpdatedUtc = item.UpdatedUtc
        }));

        db.Budgets.AddRange(snapshot.Budgets.Select(item => new Budget
        {
            Id = item.Id,
            ProjectId = item.ProjectId,
            ServiceId = item.ServiceId,
            Name = item.Name,
            Period = item.Period,
            Amount = item.Amount,
            Currency = item.Currency,
            Thresholds = item.Thresholds,
            IsEnabled = item.IsEnabled,
            CreatedUtc = item.CreatedUtc,
            UpdatedUtc = item.UpdatedUtc
        }));

        db.Reminders.AddRange(snapshot.Reminders.Select(item => new Reminder
        {
            Id = item.Id,
            Title = item.Title,
            Notes = item.Notes,
            Type = item.Type,
            Status = item.Status,
            CloudAccountId = localReminderAccounts.GetValueOrDefault(item.Id),
            CustomTarget = item.CustomTarget,
            DueDate = item.DueDate,
            LeadTimesDays = item.LeadTimesDays,
            Channels = item.Channels,
            RecurrenceDays = item.RecurrenceDays,
            SnoozedUntilUtc = item.SnoozedUntilUtc,
            LastFiredUtc = item.LastFiredUtc,
            CreatedUtc = item.CreatedUtc,
            UpdatedUtc = item.UpdatedUtc
        }));

        db.CostRecords.AddRange(snapshot.CostRecords.Select(item => new CostRecord
        {
            Id = item.Id,
            ServiceId = item.ServiceId,
            Date = item.Date,
            Amount = item.Amount,
            Currency = item.Currency,
            Source = item.Source,
            SyncedUtc = item.SyncedUtc,
            CreatedUtc = item.CreatedUtc,
            UpdatedUtc = item.UpdatedUtc
        }));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

public sealed record SaasSyncResult(
    bool Success,
    DateTimeOffset? ServerUtc = null,
    int RecordsHydrated = 0,
    string? Error = null)
{
    public static SaasSyncResult Ok(DateTimeOffset serverUtc, int count)
        => new(true, serverUtc, count);

    public static SaasSyncResult Fail(string error)
        => new(false, Error: error);
}
