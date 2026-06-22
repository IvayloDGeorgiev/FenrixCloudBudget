# Migrations

This folder holds EF Core migrations. The **initial migration must be generated on
your Windows machine** (the build sandbox has no .NET SDK), then it travels with the repo.

Generate it once:

```powershell
# from the repo root
dotnet tool install --global dotnet-ef        # first time only
dotnet ef migrations add InitialCreate `
  --project src/FenrixCloudBudget.Data `
  --startup-project src/FenrixCloudBudget.Api
```

`DesignTimeDbContextFactory` targets SQLite, so no live database is needed to scaffold
the migration. At runtime each `IDataProvider.InitializeAsync()` calls `Database.Migrate()`
to apply them (SQLite by default; the same model also applies to SQL Server / Cloud SQL).
