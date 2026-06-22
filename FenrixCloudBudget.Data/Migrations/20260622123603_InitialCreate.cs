using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FenrixCloudBudget.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThemeId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FollowSystemDarkMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataMode = table.Column<int>(type: "INTEGER", nullable: false),
                    ConnectionStringReference = table.Column<string>(type: "TEXT", nullable: true),
                    AuthMode = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncIntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: true),
                    ContactName = table.Column<string>(type: "TEXT", nullable: true),
                    ContactEmail = table.Column<string>(type: "TEXT", nullable: true),
                    ContactPhone = table.Column<string>(type: "TEXT", nullable: true),
                    BillingAddress = table.Column<string>(type: "TEXT", nullable: true),
                    BillingEmail = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CloudAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountOrSubscriptionId = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<string>(type: "TEXT", nullable: true),
                    CredentialReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SecretHint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    FromName = table.Column<string>(type: "TEXT", nullable: true),
                    FromAddress = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    CredentialReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SecretHint = table.Column<string>(type: "TEXT", nullable: true),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    EnabledChannels = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastTestedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    QuietHoursFrom = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    QuietHoursTo = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", nullable: true),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    FiredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Blob = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastLoginUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CloudAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomTarget = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LeadTimesDays = table.Column<string>(type: "TEXT", nullable: false),
                    Channels = table.Column<int>(type: "INTEGER", nullable: false),
                    RecurrenceDays = table.Column<int>(type: "INTEGER", nullable: false),
                    SnoozedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastFiredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reminders_CloudAccounts_CloudAccountId",
                        column: x => x.CloudAccountId,
                        principalTable: "CloudAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCloudAccounts",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    CloudAccountId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCloudAccounts", x => new { x.ProjectId, x.CloudAccountId });
                    table.ForeignKey(
                        name: "FK_ProjectCloudAccounts_CloudAccounts_CloudAccountId",
                        column: x => x.CloudAccountId,
                        principalTable: "CloudAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectCloudAccounts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceType = table.Column<string>(type: "TEXT", nullable: true),
                    Tier = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalResourceId = table.Column<string>(type: "TEXT", nullable: true),
                    CloudAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EstimatePeriod = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Services_CloudAccounts_CloudAccountId",
                        column: x => x.CloudAccountId,
                        principalTable: "CloudAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Services_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Period = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Thresholds = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Budgets_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CostRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostRecords_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BudgetId = table.Column<int>(type: "INTEGER", nullable: true),
                    ThresholdPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    Channels = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastFiredPeriodKey = table.Column<string>(type: "TEXT", nullable: true),
                    LastFiredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_BudgetId",
                table: "Alerts",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_ProjectId",
                table: "Budgets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_ServiceId",
                table: "Budgets",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Name",
                table: "Clients",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CostRecords_ServiceId_Date",
                table: "CostRecords",
                columns: new[] { "ServiceId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_Email",
                table: "Invitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_DedupeKey",
                table: "NotificationDeliveries",
                column: "DedupeKey");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_FiredUtc",
                table: "NotificationDeliveries",
                column: "FiredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCloudAccounts_CloudAccountId",
                table: "ProjectCloudAccounts",
                column: "CloudAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ClientId",
                table: "Projects",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_CloudAccountId",
                table: "Reminders",
                column: "CloudAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_DueDate",
                table: "Reminders",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_SecretEntries_Reference",
                table: "SecretEntries",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_CloudAccountId",
                table: "Services",
                column: "CloudAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_ExternalResourceId",
                table: "Services",
                column: "ExternalResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_ProjectId",
                table: "Services",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "CostRecords");

            migrationBuilder.DropTable(
                name: "EmailConfigs");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "ProjectCloudAccounts");

            migrationBuilder.DropTable(
                name: "Reminders");

            migrationBuilder.DropTable(
                name: "SecretEntries");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "CloudAccounts");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
