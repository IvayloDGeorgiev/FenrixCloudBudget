# Implementation progress log

> Living checklist tracking where the build is, so work can resume after any interruption
> (hit a limit, PC off, new session). Update the **status** and **next step** as you go.
> The full design rationale lives in `IMPLEMENTATION_PLAN.md`.

**Last updated:** 2026-06-22
**Current focus:** Phases 4–5 implemented (via Codex). Latest change: Projects page now supports editing/removing **manual** services (cloud-synced services are locked) and full **per-project budget** CRUD so pay-as-you-go spend triggers threshold alerts before overspend. Next: manual Windows/API verification, then complete hosted OTP sign-in and bidirectional SaaS sync.

### Recent: dashboard as a "budget radar"
- ✅ `DashboardAnalytics` service computes everything in one pass: budget **pacing** (actual vs ideal vs forecast), **projected month-end**, **budget runway / days-to-exhaust**, **month-over-month**, budget **health** counts, spend trend + **anomaly** flags (mean+2σ), **cost composition over time** by provider, **spend by client/project**, **top movers** (vs previous period), a **treemap** (project→service), and an **estimated-vs-synced** split. Covered by `DashboardAnalyticsTests`.
- ✅ New chart components: `FxPacingChart`, `FxStackedArea`, `FxRankBars`, `FxDeltaBars`, `FxTreemap`; `FxAreaChart` extended with anomaly markers.
- ✅ Dashboard rebuilt: KPI row (spend, projected month-end, runway, MoM) → pacing hero (+health chips) → budget guardrails → provider mix/top drivers → movers/composition → by client/project → cost map (treemap) → spend trend (bottom, with anomaly flags + data-quality %).
- Deferred (needs more data): **unallocated/ungrouped spend** (requires account-level totals beyond per-service records), idle/waste, commitment coverage.

### Recent: modern dashboard charts
- ✅ Replaced MudChart with custom animated, interactive SVG/CSS components in `Components/Shared`:
  `FxDonutChart` (provider mix, draw-in + hover highlight + centre total), `FxBarChart` (top cost
  drivers, grow-in bars + hover tooltips), `FxAreaChart` (new "Spend trend" daily card, left-to-right
  reveal + point tooltips), and `FxSparkline` (per-project mini trend in the budget table — "a chart
  per project"). No JS charting dependency; works in the WebView and respects theme tokens.
- Possible next: a dedicated per-project detail view with its own larger trend + service breakdown.

### Recent: test data mode (testing aid)
- ✅ Isolated **test database** (`fenrix.test.db`) + `RoutingDbContextFactory` so the whole app reads/writes test data when the toggle is on — even if a real DB/SQL Server is connected. Real data is never touched.
- ✅ `TestDataSeeder`: 5 clients × 2–3 projects, mixed manual/connected services, budgets, ~30 days of cost records, sample cloud accounts + reminders, and the bootstrap admin (so sign-in works in test mode).
- ✅ Toggle in **Settings → Data & connections**; persisted via MAUI Preferences and restored on startup. Real backend is always initialized first regardless of the toggle.
- ✅ Tests: `TestDataSeederTests` (5 clients, populated, idempotent).
- Note: no migration needed (test DB uses `EnsureCreated`); `*.db` is gitignored.

### Recent: project services & budgets (UX request)
- ✅ Manual services are inline-editable (name/provider/est. cost) and removable on the Projects editor.
- ✅ Cloud-synced services are locked (read-only, lock icon) — managed via Cloud Services.
- ✅ Per-project budgets: add/edit/remove with amount, period, and alert thresholds; surfaced on project tiles.
- ✅ `SaveAsync` reconciles the tracked entity (EF detached `Update` does not delete removed children), so removals persist.
- Note: no DB schema change (Budget/Service tables already existed) — no new migration required.

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
- ✅ JWT authentication foundation: signed access tokens, protected `/auth/me`, Admin-role authorization, rate-limited password/OTP entry points, and protected user/sync endpoints.
- ✅ OTP and invitations: hashed expiring single-use codes, attempt limits, invited-user activation, role assignment, invitation creation/revocation, and server-side email dispatch through the shared notification service.
- ✅ Local bootstrap sign-in: the database seeds `admin@fenrix.local` (`admin` / `123456`), the branded splash remains visible for an additional second, and unauthenticated app launches now stop at a login form.
- ✅ Admin-only Users workspace: Users navigation is hidden from members; Administrators and Members have separate searchable sections; admins can promote/demote, enable/disable, invite, revoke invitations, and delete ordinary users.
- ✅ Lockout protection: current/final active administrators cannot be disabled, demoted, or deleted. The seeded administrator keeps its Admin role, can be enabled/disabled by another active admin, and cannot be deleted or re-invited.
- 🟡 SaaS data provider: authenticated credential-free server snapshot and local SQLite pull hydration are implemented. Push, conflict resolution, tenant ownership, and stable cross-device identifiers remain.
- 🟡 Verification: the earlier Phase 5 API/auth/SaaS slice last passed 14 tests. A bootstrap-account test and the latest login/user UI changes have been added but intentionally not built or run yet; manual verification is requested.
- ⬜ Hosted app sign-in UI for invited members (OTP request/verification), production password replacement/change flow, push sync, and optional B2C (Entra/Cognito).
- **Next:** manually build and test Windows/API login and Users actions; then wire invited-member OTP screens and define tenant-scoped stable IDs before enabling SaaS push.

## Phase 6 — Monetization & polish
- ⬜ Remove-ads / Pro via Play Billing; iOS/macOS targets; anomaly detection; forecasts.

---

## Known cross-cutting TODOs
- ✅ Reminder lifecycle controls are reversible: Active → Completed, Snoozed/Completed/Dismissed → Active; reminders can be permanently deleted after confirmation.
- ✅ All inline app dialogs use the shared conditionally rendered modal shell, avoiding stale MudDialog visibility callbacks; Cancel, Save/Send/Connect, backdrop click, Escape, and delete confirmation share one close lifecycle.
- ✅ Responsive UI pass: reminder/client/resource tables were replaced or adapted for cards, forms and dashboard filters reflow across desktop/tablet/mobile breakpoints, actions become touch-friendly, and the bottom navigation retains every authorized destination including Users and Settings.
- Replace placeholder brand SVGs in `FenrixCloudBudget.App/Resources/` with final art.
- Amazon SES + Azure Communication Services email adapters expose their field schema but need their SDK packages wired (marked TODO in `PendingCloudEmailSenders.cs`).
- Settings → "Test connection" for SQL Server runs on the live provider in the desktop build (placeholder text in UI).
- Review/upgrade dependencies currently producing NuGet vulnerability warnings (`SQLitePCLRaw.lib.e_sqlite3`, `System.Security.Cryptography.Xml`, and transitive `System.Drawing.Common`) plus the AndroidX lifecycle version constraint warning.
