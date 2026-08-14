# .NET 10 Breaking-Changes Re-Scrape (task 001, step 3)

> **Date**: 2026-08-11 · **By**: `researcher` subagent (dispatched by task 001) · **Baseline**: design §5 (hit-sites H1–H6 + secondary audits)
> **Verdict**: ✅ **No scope change. Task 001 closes. No escalation (CLAUDE.md §6 bar not met).**

## Why this re-scrape exists

At original research time (2026-08-10) the Microsoft Learn .NET 10 breaking-changes page carried a "work in progress" banner (design §12). This step re-checked it (plus ASP.NET Core 10 and C# 14) for late servicing entries before the retarget proper.

## Headline

The Learn pages have **not been re-serviced** since the original research:

| Page | `updated_at` | vs 2026-08-10 scrape |
|---|---|---|
| .NET 10 core compatibility | 2026-07-30 | unchanged (banner still present) |
| ASP.NET Core 10 breaking changes | 2026-01-28 | unchanged |
| C# 14 what's-new | 2025-11-26 | unchanged |

**Zero genuinely-new servicing entries landed.** The delta below is against what §5 *transcribed* — items already present on the page that §5 omitted but that plausibly touch Spaarke.

## New entries beyond design §5 (all pre-existing on the page, none new)

| Entry | Affected Spaarke path | Severity | Disposition |
|---|---|---|---|
| `ActivitySource.CreateActivity/StartActivity` sampling behavior change | Real — `ActivitySource` in ~8 server files (7 `Telemetry/*.cs` + `TelemetryModule.cs`) | Medium (observability) | Fold into P1 sweep; **verify trace emission in P5 slot smoke** |
| Default trace-context propagator now W3C standard | OTel/Azure Monitor correlation | Low–Medium (likely no-op; OTel sets own propagators) | P5 smoke: confirm end-to-end trace continuity |
| C# 14 overload resolution with span parameters | Any call site where `Span`/`ReadOnlySpan` + array overloads coexist (silent bind shift) | Low–Medium | Quick grep during P1; watch in build+review |
| `XmlSerializer` no longer ignores `[Obsolete]` properties | Only `ComposeService.cs` uses `XmlSerializer` | Low | Verify no obsolete-marked serialized property |

## Confirmed N/A (present on page, verified against tree)

- Containers default to Ubuntu images — no Dockerfile (framework-dependent publish to App Service Linux).
- Runtime no longer provides default SIGTERM handlers — generic host (`WebApplication`), no custom SIGTERM/`ApplicationStopping` hooks → graceful shutdown unaffected.
- Streaming HTTP responses in browser HTTP clients — server, not Blazor WASM.
- Cookie login redirects disabled for known API endpoints — BFF is bearer/OBO, not cookie auth.
- `WithOpenApi` / `WebHostBuilder` / `IActionContextAccessor` / Razor runtime compilation obsoletions — minimal API; already ruled absent.

## Recommended follow-ups (carried into later phases — NOT task-001 blockers)

1. **P5 (task 051 smoke)**: add line item — *"ActivitySource sampling + W3C propagator: verify trace continuity in Azure Monitor."* This is the only genuinely-new production-path hit and is best **proven** (behavioral telemetry-correlation change), not reasoned.
2. **P1 (secondary sweep)**: quick grep for span/array overload ambiguity at hot call sites (encoding/string/stream APIs) — low effort, closes the C# 14 overload-resolution item. Also spot-check `ComposeService.cs` XmlSerializer for `[Obsolete]`-marked serialized props.
3. **Near cutover (~2026-11-10)**: when the "work in progress" banner is removed from the .NET 10 core page, a final ~10-min re-scrape is cheap insurance. Not required now.

## Sources (retrieved 2026-08-11)

- https://learn.microsoft.com/en-us/dotnet/core/compatibility/10
- https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview
- https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14
- Live-tree greps: `ActivitySource` (18 files, ~8 server-telemetry), `XmlSerializer` (1 file), no Dockerfile, no SIGTERM hooks
