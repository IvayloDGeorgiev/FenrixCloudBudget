using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace FenrixCloudBudget.Data.TestData;

/// <summary>
/// Seeds a rich, deterministic dataset into the isolated test database: 5 clients, each with
/// 2–3 projects containing a mix of manual and cloud-synced services, budgets, ~30 days of cost
/// records (so dashboards populate), plus sample cloud accounts and reminders. Idempotent: it
/// only seeds when the test database is empty.
/// </summary>
public static class TestDataSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Clients.AnyAsync(ct)) return;

        var rng = new Random(42); // deterministic so test runs are repeatable
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!await db.AppSettings.AnyAsync(ct))
            db.AppSettings.Add(new AppSetting());
        if (!await db.EmailConfigs.AnyAsync(ct))
            db.EmailConfigs.Add(new EmailConfig { Method = EmailMethod.InAppAndDeviceOnly, Enabled = true });

        // Mirror the real bootstrap admin so sign-in works while in test-data mode.
        if (!await db.Users.AnyAsync(u => u.Email == SeededAdminAccount.Email, ct))
        {
            db.Users.Add(new User
            {
                Email = SeededAdminAccount.Email,
                DisplayName = SeededAdminAccount.DisplayName,
                Role = UserRole.Admin,
                Status = UserStatus.Active
            });
        }

        // Sample connected accounts (so connected services + reminders have something to link to).
        var accounts = new[]
        {
            new CloudAccount { DisplayName = "Aws – Shared Prod", Provider = CloudProvider.Aws, AccountOrSubscriptionId = "111122223333", SecretHint = "AKIA••••••••", LastSyncedUtc = DateTimeOffset.UtcNow.AddHours(-6) },
            new CloudAccount { DisplayName = "Azure – Main Sub", Provider = CloudProvider.Azure, AccountOrSubscriptionId = "00000000-0000-0000-0000-000000000001", SecretHint = "9f3a••••••••", LastSyncedUtc = DateTimeOffset.UtcNow.AddHours(-9) },
            new CloudAccount { DisplayName = "GCP – Analytics", Provider = CloudProvider.Gcp, AccountOrSubscriptionId = "fenrix-analytics", SecretHint = "{\"ty••••••••", LastSyncedUtc = DateTimeOffset.UtcNow.AddDays(-1) }
        };
        db.CloudAccounts.AddRange(accounts);

        var clientNames = new[]
        {
            ("Acme Studios", "Acme Studios Ltd", "ops@acme.example", "USD"),
            ("Nimbus Retail", "Nimbus Retail Group", "cloud@nimbus.example", "EUR"),
            ("Orbit Health", "Orbit Health Inc", "it@orbithealth.example", "USD"),
            ("Vela Fintech", "Vela Fintech AD", "devops@vela.example", "BGN"),
            ("Pioneer Media", "Pioneer Media LLP", "infra@pioneer.example", "GBP")
        };

        var projectThemes = new[]
        {
            ("Web Platform", new[] { "Web servers", "Primary database", "Object storage", "CDN" }),
            ("Mobile Backend", new[] { "API gateway", "Auth service", "Push pipeline" }),
            ("Data Pipeline", new[] { "Ingestion cluster", "Warehouse", "Scheduler" }),
            ("Internal Tools", new[] { "Admin app", "Reporting DB" }),
            ("ML Workloads", new[] { "Training nodes", "Feature store", "Model registry" })
        };

        var providers = new[] { CloudProvider.Aws, CloudProvider.Azure, CloudProvider.Gcp };

        foreach (var (name, company, email, currency) in clientNames)
        {
            var client = new Client
            {
                Name = name,
                CompanyName = company,
                ContactEmail = email,
                ContactName = "Account Owner",
                Notes = "Test data — generated for feature testing.",
                Tags = "test"
            };

            var projectCount = rng.Next(2, 4); // 2 or 3
            for (var p = 0; p < projectCount; p++)
            {
                var (themeName, serviceNames) = projectThemes[rng.Next(projectThemes.Length)];
                var project = new Project
                {
                    Name = $"{name.Split(' ')[0]} {themeName}",
                    Description = $"{themeName} workloads for {company}.",
                    Currency = currency,
                    Status = ProjectStatus.Active,
                    Client = client
                };

                decimal projectMonthly = 0m;
                var serviceTake = Math.Min(serviceNames.Length, rng.Next(2, serviceNames.Length + 1));
                for (var s = 0; s < serviceTake; s++)
                {
                    var provider = providers[rng.Next(providers.Length)];
                    var connected = rng.NextDouble() < 0.55; // ~half pulled from cloud
                    var monthly = Math.Round((decimal)(rng.Next(15, 480) + rng.NextDouble()), 2);
                    projectMonthly += monthly;

                    var account = accounts.FirstOrDefault(a => a.Provider == provider);
                    var service = new Service
                    {
                        Name = serviceNames[s],
                        Provider = provider,
                        ServiceType = ServiceTypeFor(provider),
                        Source = connected ? ServiceSource.Connected : ServiceSource.Manual,
                        ExternalResourceId = connected ? $"res-{Guid.NewGuid():N}".Substring(0, 16) : null,
                        CloudAccount = connected ? account : null,
                        EstimatedCost = monthly,
                        EstimatePeriod = BudgetPeriod.Monthly,
                        Currency = currency
                    };

                    // ~30 days of daily cost records for connected services (drives synced dashboard).
                    if (connected)
                    {
                        var daily = monthly / 30m;
                        for (var d = 29; d >= 0; d--)
                        {
                            var jitter = (decimal)(0.7 + rng.NextDouble() * 0.6); // 0.7x–1.3x
                            service.CostRecords.Add(new CostRecord
                            {
                                Date = today.AddDays(-d),
                                Amount = Math.Round(daily * jitter, 2),
                                Currency = currency,
                                Source = ServiceSource.Connected,
                                SyncedUtc = DateTimeOffset.UtcNow.AddHours(-rng.Next(1, 12))
                            });
                        }
                    }

                    project.Services.Add(service);
                }

                // A monthly budget a little above current estimate so thresholds are meaningful.
                var cap = Math.Ceiling((projectMonthly * 1.2m) / 50m) * 50m;
                project.Budgets.Add(new Budget
                {
                    Name = "Monthly cap",
                    Period = BudgetPeriod.Monthly,
                    Amount = cap <= 0 ? 100m : cap,
                    Currency = currency,
                    Thresholds = "50,80,100",
                    IsEnabled = true
                });

                client.Projects.Add(project);
            }

            db.Clients.Add(client);
        }

        // A few reminders, some linked to connected accounts.
        db.Reminders.AddRange(
            new Reminder
            {
                Title = "Rotate Azure app client secret",
                Type = ReminderType.ClientSecret,
                CloudAccount = accounts[1],
                DueDate = DateTimeOffset.UtcNow.AddDays(9),
                LeadTimesDays = "30,14,7,1",
                Channels = NotificationChannel.InApp | NotificationChannel.LocalDevice,
                Status = ReminderStatus.Active
            },
            new Reminder
            {
                Title = "TLS certificate renewal (api.acme.example)",
                Type = ReminderType.Certificate,
                CloudAccount = accounts[0],
                DueDate = DateTimeOffset.UtcNow.AddDays(23),
                LeadTimesDays = "30,14,7,1",
                Status = ReminderStatus.Active
            },
            new Reminder
            {
                Title = "Review quarterly cloud budgets",
                Type = ReminderType.Custom,
                DueDate = DateTimeOffset.UtcNow.AddDays(40),
                LeadTimesDays = "14,3",
                RecurrenceDays = 90,
                Status = ReminderStatus.Active
            });

        await db.SaveChangesAsync(ct);
    }

    private static string ServiceTypeFor(CloudProvider provider) => provider switch
    {
        CloudProvider.Aws => "EC2 / RDS / S3",
        CloudProvider.Azure => "App Service / Azure SQL",
        _ => "Compute Engine / BigQuery"
    };
}
