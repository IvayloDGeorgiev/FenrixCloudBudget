using FenrixCloudBudget.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FenrixCloudBudget.Data.Configurations;

// Decimal precision is set explicitly so SQL Server / Cloud SQL don't warn or truncate.
internal static class Money
{
    public const string Type = "decimal(18,2)";
}

public class ClientConfig : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Name);
    }
}

public class ProjectConfig : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.HasOne(x => x.Client)
            .WithMany(c => c.Projects)
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProjectCloudAccountConfig : IEntityTypeConfiguration<ProjectCloudAccount>
{
    public void Configure(EntityTypeBuilder<ProjectCloudAccount> b)
    {
        b.HasKey(x => new { x.ProjectId, x.CloudAccountId });
        b.HasOne(x => x.Project).WithMany(p => p.CloudAccounts).HasForeignKey(x => x.ProjectId);
        b.HasOne(x => x.CloudAccount).WithMany(c => c.Projects).HasForeignKey(x => x.CloudAccountId);
    }
}

public class CloudAccountConfig : IEntityTypeConfiguration<CloudAccount>
{
    public void Configure(EntityTypeBuilder<CloudAccount> b)
    {
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        // CredentialReference points to the secure store; the secret itself is never in this table.
        b.Property(x => x.CredentialReference).HasMaxLength(200);
        b.Property(x => x.SecretHint).HasMaxLength(64);
    }
}

public class ServiceConfig : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.EstimatedCost).HasColumnType(Money.Type);
        b.HasOne(x => x.Project).WithMany(p => p.Services).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.CloudAccount).WithMany().HasForeignKey(x => x.CloudAccountId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.ExternalResourceId);
    }
}

public class BudgetConfig : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> b)
    {
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.Amount).HasColumnType(Money.Type);
        b.HasOne(x => x.Project).WithMany(p => p.Budgets).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class CostRecordConfig : IEntityTypeConfiguration<CostRecord>
{
    public void Configure(EntityTypeBuilder<CostRecord> b)
    {
        b.Property(x => x.Amount).HasColumnType(Money.Type);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.HasOne(x => x.Service).WithMany(s => s.CostRecords).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ServiceId, x.Date }).IsUnique();
    }
}

public class AlertConfig : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> b)
    {
        b.HasOne(x => x.Budget).WithMany().HasForeignKey(x => x.BudgetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReminderConfig : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> b)
    {
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.HasOne(x => x.CloudAccount).WithMany(c => c.Reminders).HasForeignKey(x => x.CloudAccountId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.DueDate);
    }
}

public class NotificationDeliveryConfig : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.Property(x => x.DedupeKey).HasMaxLength(200);
        b.HasIndex(x => x.DedupeKey);
        b.HasIndex(x => x.FiredUtc);
    }
}

public class EmailConfigConfig : IEntityTypeConfiguration<EmailConfig>
{
    public void Configure(EntityTypeBuilder<EmailConfig> b)
    {
        b.Property(x => x.FromAddress).HasMaxLength(320);
        b.Property(x => x.CredentialReference).HasMaxLength(200);
    }
}

public class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Email).IsRequired().HasMaxLength(320);
        b.HasIndex(x => x.Email).IsUnique();
    }
}

public class InvitationConfig : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.Property(x => x.Email).IsRequired().HasMaxLength(320);
        b.Property(x => x.CodeHash).IsRequired().HasMaxLength(128);
        b.HasIndex(x => x.Email);
    }
}

public class AppSettingConfig : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.Property(x => x.ThemeId).HasMaxLength(40);
        b.Property(x => x.DefaultCurrency).HasMaxLength(3);
    }
}
