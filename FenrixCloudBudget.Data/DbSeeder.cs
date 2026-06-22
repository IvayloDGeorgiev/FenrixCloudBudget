using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data;

/// <summary>Seeds first-run defaults: the singleton AppSetting, default EmailConfig, and demo data.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.AppSettings.AnyAsync(ct))
            db.AppSettings.Add(new AppSetting());

        if (!await db.EmailConfigs.AnyAsync(ct))
            db.EmailConfigs.Add(new EmailConfig { Method = EmailMethod.InAppAndDeviceOnly, Enabled = true });

        // Lightweight demo so the dashboard isn't empty on first launch.
        if (!await db.Clients.AnyAsync(ct))
        {
            var client = new Client { Name = "Acme Studios", CompanyName = "Acme Studios Ltd", ContactEmail = "ops@acme.example" };
            var project = new Project
            {
                Name = "Acme Web Platform",
                Description = "Demo project — replace with your own.",
                Currency = "USD",
                Client = client,
                Services =
                {
                    new Service { Name = "App servers (EC2)", Provider = CloudProvider.Aws, ServiceType = "EC2 t3.medium", EstimatedCost = 120m, Currency = "USD" },
                    new Service { Name = "Database (Azure SQL)", Provider = CloudProvider.Azure, ServiceType = "Azure SQL S1", EstimatedCost = 75m, Currency = "USD" },
                    new Service { Name = "Storage (GCS)", Provider = CloudProvider.Gcp, ServiceType = "Cloud Storage", EstimatedCost = 18m, Currency = "USD" }
                },
                Budgets = { new Budget { Name = "Monthly cap", Period = BudgetPeriod.Monthly, Amount = 250m, Currency = "USD", Thresholds = "50,80,100" } }
            };
            db.Clients.Add(client);
            db.Projects.Add(project);
        }

        await db.SaveChangesAsync(ct);
    }
}
