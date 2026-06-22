# Implementation progress log

> Living checklist tracking where the build is, so work can resume after any interruption
> (hit a limit, PC off, new session). Update the **status** and **next step** as you go.
> The full design rationale lives in `IMPLEMENTATION_PLAN.md`.

**Last updated:** 2026-06-22
**Current focus:** Phase 4 code path implemented and tested. Next: validate against live AWS/Azure/GCP accounts and add app-registration secret/certificate expiry metadata.

Legend: ✅ done · 🟡 partial/scaffolded · ⬜ not started

---

## Phase 0 — Foundations & spike
- ✅ Solution + 7 projects, flat layout at repo root, `Directory.Build.props`, `global.json`, `.gitignore`.
- ✅ EF Core + SQLite wired (`AppDbContext`, providers, `DesignTimeDbContextFactory`).
- 🟡 Risky-integration spikes (one real AWS/Azure/GCP call, AdMob banner) — **left to do on-device/live accounts**; connector code is written but unverified against real clouds from here.
- ✅ Initial EF migration generated and applied successfully to a clean SQLite database; full Windows + Android solution build succeeds.
- **Next:** launch the Windows app and validate the live-cloud/AdMob integrations with real credentials/device IDs.

## Phase 1 — Core app shell & manual budgeting
- ✅ Splash, tile landing (`Home.razor`), nav shell, Aurora theme (+3 more themes).
- ✅ Clients CRUD; Projects CRUD with manual services; Reminders (lead-times, snooze/done).
- ✅ Dashboard v1 (provider/client/date filters; donut, budget-vs-actual, top-services).
- ✅ Shared notification service: in-app + local device; budget + reminder evaluators; scheduler.
- 🟡 Quick-add-client-from-project flow: Projects page picks an existing client; inline quick-add dialog still to wire.
- **Next:** verify the app launches on Windows; polish the project→service "choose existing" entry point (stub present).

## Phase 2 — Polish, theming, Play Store
- ✅ 2026 visual redesign: atmospheric responsive shell, animated/reduced-motion-aware surfaces, redesigned home/dashboard, and 4 full visual personalities (Daybreak, Nebula, Graphite, Tide) with matching MudBlazor palettes and live Appearance previews.
- ✅ Settings → Appearance persists the selected personality immediately; legacy theme IDs migrate automatically. Settings → Data & Connections (mode + interval); Settings → Email & Notifications (dynamic per-method fields, test button).
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
- **Next:** auto-fill reminder expiry from app-registration secret/certificate metadata.
- ✅ `InitialCreate` migration includes `CloudAccount.OptionsJson` and the Phase 4 daily-cost index.

## Phase 4 — Cost sync & live dashboards
- ✅ `GetCostsAsync` implemented for AWS, Azure, and GCP. Azure uses the Cost Management `/query` API with bearer auth, pagination, and documented 429/503 retry headers; AWS pagination and inclusive date handling fixed.
- ✅ Background `CostSyncService`: rolling 62-day cache replacement, configurable interval, 12-hour minimum automatic AWS polling, per-account failure isolation, scheduler integration, and no duplicate secret rows during re-authentication.
- ✅ Provider costs map to connected services by resource ID where available, with provider service-name aliases and even splitting for account-level AWS service costs.
- ✅ Dashboard v2 reads synced `CostRecord` time-series for its date filters, falls back to estimates only for unsynced/manual services, shows freshness, and offers forced manual refresh.
- ✅ Phase 4 sync/re-auth and mixed actual/estimate budget tests added; full suite passes (10 tests).
- **Next:** validate all three connectors against live billing data; review any unmatched-cost rows and expand provider aliases as real account data reveals them.

## Phase 5 — Multi-user, backend & auth
- ✅ API skeleton: `/health`, `/auth/request` + `/auth/verify` (hashed, expiring, rate-limited OTP), `/api` sync read endpoints.
- 🟡 OTP issues a user but **does not yet mint a session token (JWT)** — marked TODO in `OtpService`/`AuthEndpoints`.
- ⬜ Invitations admin UI; SaaS data provider sync (push/pull deltas); server-side email provider; optional B2C (Entra/Cognito).
- **Next:** add JWT issuance + `[Authorize]` wiring; implement SaaSDataProvider sync; Users page invite flow.

## Phase 6 — Monetization & polish
- ⬜ Remove-ads / Pro via Play Billing; iOS/macOS targets; anomaly detection; forecasts.

---

## Known cross-cutting TODOs
- ✅ Reminder lifecycle controls are reversible: Active → Completed, Snoozed/Completed/Dismissed → Active; reminders can be permanently deleted after confirmation.
- Replace placeholder brand SVGs in `FenrixCloudBudget.App/Resources/` with final art.
- Amazon SES + Azure Communication Services email adapters expose their field schema but need their SDK packages wired (marked TODO in `PendingCloudEmailSenders.cs`).
- Settings → "Test connection" for SQL Server runs on the live provider in the desktop build (placeholder text in UI).
- Review/upgrade dependencies currently producing NuGet vulnerability warnings (`SQLitePCLRaw.lib.e_sqlite3`, `System.Security.Cryptography.Xml`, and transitive `System.Drawing.Common`) plus the AndroidX lifecycle version constraint warning.
