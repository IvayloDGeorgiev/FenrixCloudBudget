# FenrixCloudBudget

> Cross-platform desktop + Android app (.NET 10 MAUI Blazor Hybrid) that groups cloud
> services into **Projects** across AWS, Azure & GCP, tracks budgets, and alerts before
> overspend — solving the "AWS/GCP have no resource groups" problem.
>
> **Tagline:** Group. Budget. Stay ahead.

**Status:** Phases 1–4 complete and Phase 5 largely complete — manual budgeting, themed UI,
notifications, live AWS/Azure/GCP account connection + resource discovery + cost sync, a budget
"radar" dashboard, and JWT auth with an admin Users workspace. Remaining: Play-Store polish
(AdMob, PDF/CSV export), invited-member OTP screens, and SaaS push sync.
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
cd <path-to>\FenrixCloudBudget   # e.g. C:\Users\<you>\Documents\FenrixCloudBudget

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

## Connecting your cloud accounts

Open **Cloud Accounts → Connect account**, pick a provider, and fill in the fields below. Use a
dedicated, **read-only** identity for each provider — Fenrix never needs write access. Credentials
are encrypted at rest (AES-256-GCM); only a masked hint (`abcd••••`) is ever shown again.

After connecting, use **Discover resources** on the account to pull in resources, then group them
into projects from **Projects → Choose existing resources**. Costs appear once a sync runs
(automatically on a schedule, or via the dashboard's manual refresh).

> Two things to know: resource **discovery** and **cost** use different services on every provider,
> so both sets of permissions below are needed. And cost data always lags real time by up to ~24h —
> that's a provider limitation, not the app.

### Amazon Web Services (AWS)

What you enter: **Access key ID**, **Secret access key**, and an optional default **Region**
(defaults to `us-east-1`). The account is identified automatically by its real 12-digit ID.

Setup:

1. In the AWS Console, **enable Cost Explorer** once (Billing → Cost Explorer). Cost data isn't
   available via the API until this is turned on.
2. Create an **IAM user** for Fenrix with **programmatic access** (or reuse a read-only one).
3. Attach a least-privilege policy granting these actions (resource `*`):
   - `ce:GetCostAndUsage` — cost queries
   - `tag:GetResources` — resource discovery (Resource Groups Tagging API)
   - `sts:GetCallerIdentity` — credential validation + account ID
4. Create an **access key** for that user and paste the key ID + secret into Fenrix.

Notes: Cost Explorer is a global service (Fenrix pins it to `us-east-1` for you), so the region
field only affects discovery. Discovery sweeps all commercial regions but the Tagging API only
returns **tagged** resources — untagged ones still show up in total spend, just not as individual
resources. Each Cost Explorer call costs ~$0.01.

### Microsoft Azure

What you enter: **Tenant ID**, **Client ID**, **Subscription ID**, and **Client secret**.

Setup:

1. In **Entra ID → App registrations**, create a new registration (single tenant is fine). Note its
   **Application (client) ID** and **Directory (tenant) ID**.
2. Under that app, go to **Certificates & secrets → New client secret**, and copy the secret
   **value** (not the ID) immediately.
3. In your **Subscription → Access control (IAM)**, assign the app these roles at **subscription
   scope**:
   - **Reader** — resource discovery (Resource Graph)
   - **Cost Management Reader** — cost queries
4. Copy the **Subscription ID** and paste all four values into Fenrix.

Notes: Fenrix validates that the subscription is actually visible to the app before saving, so if
the role assignments haven't propagated yet you'll get a clear message — wait a minute and retry.

### Google Cloud Platform (GCP)

What you enter: **Project ID**, **Service account key (JSON)**, and — only if you want cost data —
**Billing export project**, **Billing export dataset**, and **Billing export table**.

Setup:

1. **Enable the Cloud Asset Inventory API** on the project (`cloudasset.googleapis.com`).
2. Create a **service account** and download its **JSON key**.
3. Grant the service account **Cloud Asset Viewer** on the project (resource discovery).
4. For costs, set up a **BigQuery billing export** (Billing → Billing export → BigQuery export). The
   export can live in a different project than your resources, which is why the billing
   project/dataset/table are entered separately. Then grant the service account:
   - **BigQuery Job User** on the *billing* project (to run the query)
   - **BigQuery Data Viewer** on the *export dataset* (to read it)
5. Paste the Project ID and the full JSON key into Fenrix. If you configured billing export, add the
   billing project (defaults to Project ID), dataset, and table name.

Notes: choose the **detailed** export table (`gcp_billing_export_resource_v1_*`) for per-resource
cost attribution; the **standard** table (`gcp_billing_export_v1_*`) gives service-level costs only.
Fenrix auto-detects which you provided. If you skip the billing export, discovery still works —
there's just no cost data to show.

---

## Git (Windows PowerShell notes)

Windows PowerShell 5.1 doesn't support `&&`, and blocks unsigned `.ps1` files by default.
Paste these blocks directly into PowerShell.

First push:

```powershell
cd <path-to>\FenrixCloudBudget   # e.g. C:\Users\<you>\Documents\FenrixCloudBudget
if (Test-Path .git) { Remove-Item -Recurse -Force .git }
git init -b main
git config user.name "<your-name>"
git config user.email "<your-email>"
git config core.autocrlf true
git remote add origin https://github.com/<your-org>/FenrixCloudBudget.git
git add .
git commit -m "Initial commit"
git push -u origin main
```

After each phase:

```powershell
cd <path-to>\FenrixCloudBudget   # e.g. C:\Users\<you>\Documents\FenrixCloudBudget
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

| Area | Now | Later |
|---|---|---|
| Clients, Projects, Services, Budgets | ✅ manual CRUD (editable/removable manual services, per-project budgets) | — |
| Dashboard | ✅ synced cost time-series + filters + budget "radar" (pacing, forecast, movers, composition, cost map, anomalies) | per-project detail view |
| Cloud connectors | ✅ connect + discover + group into projects + **cost sync (AWS/Azure/GCP)** | live-account validation against real billing |
| Reminders | ✅ manual + lead-times + snooze/done + link to connected account | auto-detect secret/cert expiry |
| Notifications | ✅ in-app + device + server email (via API) | configure email adapter SDKs (SES/ACS) |
| Auth / multi-user | ✅ JWT login + OTP/invites + admin Users workspace | invited-member OTP screens; SaaS push sync; optional B2C |
| Monetization | none — ad-free, no Pro tier | — |
| Source code | "Fenrix Source_" link (nav + Overview) opens fenrixsource.com to buy the source | — |
| Reports | ✅ executive-summary export (PDF + CSV) from the Reports page | scheduled/emailed reports |
```
