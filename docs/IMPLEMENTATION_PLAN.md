# FenrixCloudBudget — Design & Implementation Plan

> This is the design reference (the original plan). For **live build status**, see `PROGRESS.md`.

> Cross-platform desktop + Android app (.NET 10 MAUI Blazor Hybrid) that lets developers group cloud services into **Projects** across AWS, Azure, and GCP, track budgets, and get overspend alerts — solving the "AWS/GCP have no resource groups" problem.

Document version: 1.0 — June 2026
Author: Ivo (design assisted)

---

## 1. Executive summary
The core idea is buildable. One MAUI Blazor Hybrid codebase targets Windows + Android (+ iOS/macOS later); free Play Store publish with AdMob; local SQLite default with user-selectable SQL Server / Cloud SQL / SaaS backends; near-real-time (not live) cost data from AWS/Azure/GCP; passwordless email OTP (needs a backend); modern themeable UI. Market gap: a cheap, developer-friendly grouping + budgeting desktop+mobile app, versus enterprise FinOps tools (CloudZero, Cloudability) and single-cloud native tools.

## 2. Name & brand
**FenrixCloudBudget** (brand root "Fenrix" + "Cloud" + "Budget"). Tagline: *Group. Budget. Stay ahead.* Package id `com.fenrix.cloudbudget`. Wolf/cloud logo direction (Fenrir).

## 3. Market wedge
Cheap/free, install-on-your-machine app that (a) groups resources into client/project buckets across all three clouds, manually OR automatically, (b) sets per-project budgets, (c) alerts before overspend.

## 4. Technology stack
.NET 10 MAUI Blazor Hybrid · MudBlazor UI · EF Core + SQLite (pluggable to SQL Server/Cloud SQL/SaaS) · AWS/Azure/GCP SDKs · OS secure storage + AES · in-app scheduler · AdMob · OTP via backend or Entra/Cognito · local + email notifications · optional ASP.NET Core backend.

### 4.1 Configurable data backend
`IDataProvider` → Sqlite (default) / SqlServer / CloudSql / SaaS. Server modes take a connection string (+ client id/secret where relevant). Secrets masked after save (`abcd••••`), encrypted at rest, with a "Test connection" button. SaaS keeps SQLite as an offline cache.

## 5. Cloud integration design
`ICloudConnector`: AuthenticateAsync / ListScopes / DiscoverResources / GetCosts.
- **AWS** — Resource Groups Tagging API (inventory) + Cost Explorer `GetCostAndUsage` (cost; ~$0.01/request, ~24h lag — cache).
- **Azure** — Resource Graph (inventory) + Cost Management Query API (cost; rate-limited, back off on 429; needs Cost Management Reader).
- **GCP** — Cloud Asset Inventory (inventory) + BigQuery billing export (cost; user enables export first, guided wizard).
Label figures "last synced X ago", offer manual refresh + configurable interval.

## 6. Data model
Client, Project, CloudAccount/Connection, Service, Budget, EstimatedCost vs ActualCost, CostRecord, Alert/Notification, Reminder (ClientSecret/Certificate/Custom), NotificationDelivery, EmailConfig, User, Invitation, AppSetting. Relationships: Client 1—* Project; Project *—* CloudAccount; Project 1—* Service; Project 1—* Budget; Service 1—* CostRecord; CloudAccount 1—* Reminder.

## 7. Authentication
Local/no-auth first (single-user). OTP via backend (hashed, expiring, rate-limited) for multi-user. Optional B2C (Entra External ID / Cognito).

### 7.1 / 7.2 Shared notification & email
One `INotificationService` (in-app / local / email). Pluggable `IEmailSender` adapters: In-app/device only (default), Custom SMTP, Resend, SendGrid, Amazon SES, Postmark, Azure Communication Services, FenrixCloud managed (SaaS). Settings renders each method's fields dynamically; secrets masked + encrypted; templates branded; de-dup + quiet hours.

## 8. UI / UX
Splash → tile landing (Dashboard, Projects, Clients, Reminders, Users, Cloud Services, Settings). Dashboard with global + per-chart filters and export. Projects create flow with manual services or "choose existing resources". Full client CRUD + quick-add. Reminders with lead-times/recurrence/snooze. Cloud Services management. Settings (Appearance, Data & Connections, Sync, Email & Notifications, Auth, About). Theming via design tokens, 3–4 themes, WCAG AA.

## 9. Monetization
Free + AdMob (banners off data-entry screens; occasional interstitials); optional Pro/remove-ads via Play Billing; Play Console requirements (AAB, signing, privacy policy, data-safety, content rating); UMP/GDPR consent.

## 10. Phases
- **0** Foundations & spike.
- **1** Core shell & manual budgeting (no cloud, no auth) — shippable.
- **2** Polish, theming, Play Store (free + ads), email methods, export.
- **3** Cloud connections & resource discovery (read-only).
- **4** Cost sync & live dashboards + alerting.
- **5** Multi-user, backend & auth (OTP, invites, SaaS, B2C).
- **6** Monetization upgrades & broader platforms.

## 11. Key risks
Cost data not truly real-time (label "last synced"); AWS per-request charges (cache/batch/daily default); Azure/GCP rate limits & GCP export friction (back-off + wizard); storing third-party secrets (encrypt + OS keystore + least-privilege); OTP needs backend; Blazor WebView on old Android (API 24+); ads vs sensitive UX; scope creep (Phases 1–2 ship with zero integrations).

## 12. Open questions
Primary target first (Android vs Windows); currency (**decided: multi-currency**); backend host; component library (**decided: MudBlazor**); auto-sync aggressiveness.
