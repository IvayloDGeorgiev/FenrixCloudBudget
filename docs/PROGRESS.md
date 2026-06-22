# Implementation progress log

> Living checklist tracking where the build is, so work can resume after any interruption
> (hit a limit, PC off, new session). Update the **status** and **next step** as you go.
> The full design rationale lives in `IMPLEMENTATION_PLAN.md`.

**Last updated:** 2026-06-22
**Current focus:** Phase 3 (cloud connect + discovery) implemented. Needs a new EF migration for `CloudAccount.OptionsJson`, then build.

Legend: ✅ done · 🟡 partial/scaffolded · ⬜ not started

---

## Phase 0 — Foundations & spike
- ✅ Solution + 7 projects, flat layout at repo root, `Directory.Build.props`, `global.json`, `.gitignore`.
- ✅ EF Core + SQLite wired (`AppDbContext`, providers, `DesignTimeDbContextFactory`).
- 🟡 Risky-integration spikes (one real AWS/Azure/GCP call, AdMob banner) — **left to do on-device**; connector code is written but unverified against live clouds from here.
- **Next:** run `dotnet ef migrations add InitialCreate`, then `dotnet build` on Windows to confirm the toolchain.

## Phase 1 — Core app shell & manual budgeting
- ✅ Splash, tile landing (`Home.razor`), nav shell, Aurora theme (+3 more themes).
- ✅ Clients CRUD; Projects CRUD with manual services; Reminders (lead-times, snooze/done).
- ✅ Dashboard v1 (provider/client/date filters; donut, budget-vs-actual, top-services).
- ✅ Shared notification service: in-app + local device; budget + reminder evaluators; scheduler.
- 🟡 Quick-add-client-from-project flow: Projects page picks an existing client; inline quick-add dialog still to wire.
- **Next:** verify the app launches on Windows; polish the project→service "choose existing" entry point (stub present).

## Phase 2 — Polish, theming, Play Store
- ✅ 4 themes + Settings → Appearance; Settings → Data & Connections (mode + interval); Settings → Email & Notifications (dynamic per-method fields, test button).
- 🟡 Email secret persistence via `ISecretStore` from Settings UI — non-secret fields save; **wire secret save/mask round-trip**.
- 🟡 AdMob: CSS ad-slot placeholder only — **integrate Plugin.MauiMTAdmob + UMP consent**.
- ⬜ Report export (PDF/CSV).
- ⬜ Play Console: signing, privacy policy, data-safety, AAB upload.
- **Next:** AdMob init + consent; secret round-trip in Settings; PDF/CSV export.

## Phase 3 — Cloud connections & resource discovery
- ✅ `ICloudConnector` + AWS/Azure/GCP implementations (discovery + cost) behind the interface, each with a `CredentialSchema`.
- ✅ Secure credential storage (AES + masked hint); non-secret fields persisted as `CloudAccount.OptionsJson` for re-auth.
- ✅ `CloudConnectionService` orchestrates connect / re-authenticate / discover (covered by `CloudConnectionServiceTests`).
- ✅ Cloud Services page: connect-account dialog (dynamic per-provider fields) + per-account "Discover resources".
- ✅ "Choose existing resources" in Projects: pick account → discover → multi-select → added as connected Services.
- 🟡 Reminders can be **linked** to a connected account (picker added); **auto-detecting the secret/cert expiry date is still TODO** (needs the cost/metadata sync in Phase 4).
- **Next (Phase 4):** implement Azure cost query + `CostSyncService`; auto-fill reminder expiry from app-registration metadata.
- **Migration needed:** `dotnet ef migrations add AddCloudAccountOptions` (or regenerate `InitialCreate` if not yet created) for the new `OptionsJson` column.

## Phase 4 — Cost sync & live dashboards
- 🟡 `GetCostsAsync` implemented for AWS + GCP; **Azure cost query is a TODO stub** (needs the /query POST + token + 429 back-off).
- ⬜ Background `CostSyncService` (interval, caching, AWS per-request cost awareness) — scheduler has the hook (`AlertSchedulerService` TODO).
- ⬜ Dashboard v2 on synced `CostRecord` time-series + "last synced" labels.
- **Next:** implement Azure cost query; add CostSyncService writing CostRecords; switch dashboard to synced data when present.

## Phase 5 — Multi-user, backend & auth
- ✅ API skeleton: `/health`, `/auth/request` + `/auth/verify` (hashed, expiring, rate-limited OTP), `/api` sync read endpoints.
- 🟡 OTP issues a user but **does not yet mint a session token (JWT)** — marked TODO in `OtpService`/`AuthEndpoints`.
- ⬜ Invitations admin UI; SaaS data provider sync (push/pull deltas); server-side email provider; optional B2C (Entra/Cognito).
- **Next:** add JWT issuance + `[Authorize]` wiring; implement SaaSDataProvider sync; Users page invite flow.

## Phase 6 — Monetization & polish
- ⬜ Remove-ads / Pro via Play Billing; iOS/macOS targets; anomaly detection; forecasts.

---

## Known cross-cutting TODOs
- Generate the initial EF migration (one-time, on Windows): see `FenrixCloudBudget.Data/Migrations/README.md`.
- Replace placeholder brand SVGs in `FenrixCloudBudget.App/Resources/` with final art.
- Amazon SES + Azure Communication Services email adapters expose their field schema but need their SDK packages wired (marked TODO in `PendingCloudEmailSenders.cs`).
- Settings → "Test connection" for SQL Server runs on the live provider in the desktop build (placeholder text in UI).
