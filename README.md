# FenrixCloudBudget

> Cross-platform desktop + Android app (.NET 10 MAUI Blazor Hybrid) that groups cloud
> services into **Projects** across AWS, Azure & GCP, tracks budgets, and alerts before
> overspend — solving the "AWS/GCP have no resource groups" problem.
>
> **Tagline:** Group. Budget. Stay ahead.

**Status:** Phases 1–3 implemented — manual budgeting + UI + settings + notifications, plus
live cloud account connection, resource discovery, and grouping resources into projects.
Cost sync (Phase 4) and the multi-user backend (Phase 5) are scaffolded behind interfaces.
See `docs/PROGRESS.md` for live per-phase status and `docs/IMPLEMENTATION_PLAN.md` for the design.

---

## Solution layout (flat)

| Project | What it is |
|---|---|
| `FenrixCloudBudget.Core` | Domain entities, enums, DTOs, and the key interfaces (`IDataProvider`, `ICloudConnector`, `INotificationService`, `IEmailSender`, `ISecretStore`). No external deps. |
| `FenrixCloudBudget.Data` | EF Core `AppDbContext`, entity configs, pluggable data providers (SQLite default / SQL Server / Cloud SQL / SaaS), seeder, AES secret table. |
| `FenrixCloudBudget.Cloud` | `ICloudConnector` implementations for AWS, Azure, GCP (resource discovery + cost queries). |
| `FenrixCloudBudget.Services` | Security (AES secret store), pluggable email adapters, the shared notification service, and the budget/reminder evaluation + scheduler. |
| `FenrixCloudBudget.App` | The .NET MAUI Blazor Hybrid app (Windows + Android). MudBlazor UI, design-token theming, all screens. |
| `FenrixCloudBudget.Api` | ASP.NET Core minimal API for Phase 5 (passwordless OTP, invitations, SaaS sync). |
| `FenrixCloudBudget.Tests` | xUnit tests (secret store, budget evaluator) against in-memory SQLite. |

Dependency direction: `App`/`Api` → `Services` → `Data` → `Core`; `Cloud` → `Core`.

---

## Prerequisites

- **.NET 10 SDK** — https://dotnet.microsoft.com/download
- **MAUI workload**: `dotnet workload install maui`
- **EF Core CLI**: `dotnet tool install --global dotnet-ef`
- For Android: Android SDK (installed with the MAUI workload / Visual Studio "MAUI" component).
- Windows + Visual Studio 2022 17.10+ recommended for the MAUI app.

---

## First-time setup

```powershell
cd C:\Users\Ivo\Documents\Claude\Projects\FenrixCloudBudget

# 1) Generate the initial EF migration (the model is SQLite-compatible at design time)
dotnet ef migrations add InitialCreate --project FenrixCloudBudget.Data --startup-project FenrixCloudBudget.Api

# 2) Build everything
dotnet build

# 3) Run the tests (no MAUI workload needed for these)
dotnet test
```

### Run the apps

```powershell
# Windows desktop (MAUI)
dotnet build FenrixCloudBudget.App -f net10.0-windows10.0.19041.0 -t:Run

# Android (device/emulator must be available)
dotnet build FenrixCloudBudget.App -f net10.0-android -t:Run

# Backend API (Phase 5)
dotnet run --project FenrixCloudBudget.Api   # then GET http://localhost:5180/health
```

The app creates and migrates its SQLite database automatically on first launch and seeds a
small demo project so the dashboard isn't empty.

---

## Git (Windows PowerShell notes)

Windows PowerShell 5.1 doesn't support `&&`, and blocks unsigned `.ps1` files by default.
Paste these blocks directly into PowerShell.

First push:

```powershell
cd C:\Users\Ivo\Documents\Claude\Projects\FenrixCloudBudget
if (Test-Path .git) { Remove-Item -Recurse -Force .git }
git init -b main
git config user.name "Ivo"
git config user.email "ivogeorgievdev@gmail.com"
git config core.autocrlf true
git remote add origin https://github.com/IvayloDGeorgiev/FenrixCloudBudget.git
git add .
git commit -m "Initial commit"
git push -u origin main
```

After each phase:

```powershell
cd C:\Users\Ivo\Documents\Claude\Projects\FenrixCloudBudget
git add .
git commit -m "Phase X: <summary>"
git push
```

---

## Key design decisions

- **Multi-currency** from day one: `Project`, `Budget`, `Service`, `CostRecord` each carry a
  currency; an `ICurrencyConverter` hook is ready for FX in Phase 4+.
- **Pluggable backends**: one `IDataProvider` abstraction; switch SQLite / SQL Server / Cloud SQL
  / SaaS in Settings.
- **Pluggable email**: one `IEmailSender` interface; the Settings form renders each adapter's
  fields from its `FieldSchema`, so adding a provider needs no UI rewrite.
- **Secrets**: AES-256-GCM at rest, master key in OS secure storage (MAUI `SecureStorage`),
  displayed as `abcd••••` after save — never round-tripped to the UI.
- **One notification layer**: budget alerts, reminders, and OTP all go through
  `INotificationService` (in-app / device / email) with de-duplication + quiet hours.
- **Theming**: CSS design tokens selected by `data-theme` (Aurora / Midnight / Slate / Mint) —
  switching is instant and adding a theme needs no code.

---

## What works now vs. later

| Area | Now (Phase 1–2) | Later |
|---|---|---|
| Clients, Projects, Services, Budgets | ✅ manual CRUD | — |
| Dashboard | ✅ from manual data + filters | synced cost time-series (Phase 4) |
| Reminders | ✅ manual + lead-times + snooze/done + link to connected account | auto-detect expiry (Phase 4) |
| Notifications | ✅ in-app + device | email once a method is configured; server email (Phase 5) |
| Cloud connectors | ✅ connect accounts + discover resources + group into projects | cost sync (Phase 4) |
| Auth / multi-user | local single-user | OTP + invites + B2C (Phase 5) |
| Ads / Pro | ad slot placeholder | AdMob + Play Billing (Phase 2/6) |
```
