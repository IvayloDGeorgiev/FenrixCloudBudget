# Migrations

This folder holds EF Core migrations. `InitialCreate` was generated on 2026-06-22 and
includes the Phase 3 `CloudAccount.OptionsJson` field plus the Phase 4 daily-cost index.

Add future migrations from the repo root:

```powershell
dotnet ef migrations add MeaningfulMigrationName `
  --project FenrixCloudBudget.Data\FenrixCloudBudget.Data.csproj `
  --startup-project FenrixCloudBudget.Data\FenrixCloudBudget.Data.csproj `
  --output-dir Migrations
```

`DesignTimeDbContextFactory` targets SQLite, so no live database is needed to scaffold
the migration. At runtime each `IDataProvider.InitializeAsync()` calls `Database.Migrate()`
to apply pending migrations.
