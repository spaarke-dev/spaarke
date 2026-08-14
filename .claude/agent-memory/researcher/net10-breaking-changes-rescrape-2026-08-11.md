---
name: net10-breaking-changes-rescrape-2026-08-11
description: Re-scrape of .NET 10 / ASP.NET Core 10 / C# 14 breaking-changes pages (2026-08-11) for dotnet-10-upgrade-r1 task 001 — diff vs design §5 baseline; no new servicing entries, but §5 omitted a few already-present entries (ActivitySource/W3C propagator observability, span overload resolution, XmlSerializer-obsolete)
metadata:
  type: project
---

## 2026-08-11: .NET 10 breaking-changes re-scrape (dotnet-10-upgrade-r1 task 001)

**Question**: Did any NEW servicing/late breaking-change entries land on the "work in progress" .NET 10 / ASP.NET Core 10 / C# 14 Learn pages since the 2026-08-10 research, beyond design §5 (H1–H6 + secondary sweep)?

**Findings**: The Learn pages have NOT been re-serviced since the original scrape — core .NET 10 page `updated_at` = 2026-07-30 (BEFORE the 2026-08-10 session), ASP.NET Core 10 = 2026-01-28, C# 14 = 2025-11-26. So zero genuinely-new entries. However, §5 only transcribed the highest-impact items; several already-present entries plausibly touch Spaarke and were NOT written down: (1) **ActivitySource.CreateActivity/StartActivity sampling behavior change** + (2) **Default trace-context propagator now W3C** — both hit the OTel/Azure Monitor observability path; Spaarke uses `ActivitySource` in ~8 files (7 `Telemetry/*.cs` classes + `TelemetryModule`). (3) **C# 14 overload resolution with span parameters** (behavioral — can shift overload selection where span+array overloads coexist). (4) **XmlSerializer no longer ignores `[Obsolete]` properties** (only `ComposeService.cs` uses XmlSerializer; low). N/A items: Containers/Ubuntu default image (no Dockerfile — FDD on App Service), SIGTERM default-handler removal (uses generic host, no custom `PosixSignalRegistration`/`ApplicationStopping` hooks found), browser HTTP streaming (server not WASM), Cookie login redirects (BFF is bearer/OBO not cookie), WithOpenApi/WebHostBuilder/IActionContextAccessor (minimal API, already verified absent).

**Verdict**: NO scope change / no new task. All delta items fold into the existing P1 secondary-audit sweep + P5 non-prod observability smoke. None is a hard blocker; observability items are behavioral telemetry-correlation changes to validate in P5, not functional breaks. Task 001 can close.

**Sources** (retrieved 2026-08-11):
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/10 (updated_at 2026-07-30)
- https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview (updated_at 2026-01-28)
- https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14 (updated_at 2025-11-26)
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/activity-sampling
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/default-trace-context-propagator
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/csharp-overload-resolution
- Live tree greps: ActivitySource (18 files), XmlSerializer (1 file), no Dockerfile, no SIGTERM/ApplicationStopping hooks

**Open questions**: Whether the W3C-propagator default actually changes Spaarke cross-service correlation given OTel sets its own propagators (likely no-op) — confirm in P5 slot smoke by checking end-to-end trace continuity in Azure Monitor.
