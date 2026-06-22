using FenrixCloudBudget.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data;

/// <summary>
/// The single EF Core context for the whole app. The same model is used across
/// SQLite (default), SQL Server and Cloud SQL — the provider is chosen at runtime
/// via IDataProvider, which configures DbContextOptions accordingly.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectCloudAccount> ProjectCloudAccounts => Set<ProjectCloudAccount>();
    public DbSet<CloudAccount> CloudAccounts => Set<CloudAccount>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<CostRecord> CostRecords => Set<CostRecord>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SecretEntry> SecretEntries => Set<SecretEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(ct);
    }

    private void StampTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedUtc = DateTimeOffset.UtcNow;
        }
    }
}
