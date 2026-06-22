using FenrixCloudBudget.Core.Enums;

namespace FenrixCloudBudget.Core.Models;

/// <summary>
/// Credential-free workspace snapshot used by the hosted API to hydrate a local SaaS cache.
/// Cloud secrets and provider options never cross this contract.
/// </summary>
public sealed record SaasWorkspaceSnapshot(
    DateTimeOffset ServerUtc,
    IReadOnlyList<SaasClientDto> Clients,
    IReadOnlyList<SaasProjectDto> Projects,
    IReadOnlyList<SaasServiceDto> Services,
    IReadOnlyList<SaasBudgetDto> Budgets,
    IReadOnlyList<SaasReminderDto> Reminders,
    IReadOnlyList<SaasCostRecordDto> CostRecords);

public sealed record SaasClientDto(
    int Id,
    string Name,
    string? CompanyName,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? BillingAddress,
    string? BillingEmail,
    string? Notes,
    string? Tags,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record SaasProjectDto(
    int Id,
    string Name,
    string? Description,
    ProjectStatus Status,
    string Currency,
    int? ClientId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record SaasServiceDto(
    int Id,
    int ProjectId,
    string Name,
    CloudProvider Provider,
    string? ServiceType,
    string? Tier,
    ServiceSource Source,
    string? ExternalResourceId,
    decimal EstimatedCost,
    BudgetPeriod EstimatePeriod,
    string Currency,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record SaasBudgetDto(
    int Id,
    int ProjectId,
    int? ServiceId,
    string Name,
    BudgetPeriod Period,
    decimal Amount,
    string Currency,
    string Thresholds,
    bool IsEnabled,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record SaasReminderDto(
    int Id,
    string Title,
    string? Notes,
    ReminderType Type,
    ReminderStatus Status,
    string? CustomTarget,
    DateTimeOffset DueDate,
    string LeadTimesDays,
    NotificationChannel Channels,
    int RecurrenceDays,
    DateTimeOffset? SnoozedUntilUtc,
    DateTimeOffset? LastFiredUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record SaasCostRecordDto(
    int Id,
    int ServiceId,
    DateOnly Date,
    decimal Amount,
    string Currency,
    ServiceSource Source,
    DateTimeOffset SyncedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);
