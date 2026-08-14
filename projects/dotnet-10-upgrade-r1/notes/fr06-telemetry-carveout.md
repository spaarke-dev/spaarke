# FR-06 — Telemetry Consolidation Carve-Out (task 014)

> **Date**: 2026-08-12 · **Task**: 014 (P1) · **Rigor**: FULL · **Model**: opus (main session; POML tier sonnet/high)
> **This is the ONE owner-approved carve-out to NFR-01** (zero-behavior-change). Everything else in `dotnet-10-upgrade-r1` is behavior-preserving; this task deliberately changes the telemetry pipeline. Cite this file in the PR's **ADR Tensions** table.
> **Verdict**: ✅ Classic App Insights SDK removed; OTel → Azure Monitor is the sole telemetry path; BFF builds green (0 errors, 21 warnings = unchanged post-013 baseline). **No live signal dropped** → escalation trigger did NOT fire.

---

## What changed (telemetry-only)

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` | Removed `<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.23.0" />` (replaced with an explanatory comment). |
| `src/server/api/Sprk.Bff.Api/Api/Agent/AgentTelemetry.cs` | Removed the classic-SDK usings (`Microsoft.ApplicationInsights`, `Microsoft.ApplicationInsights.DataContracts`), the `TelemetryClient? _telemetryClient` field, the optional ctor param, and all `_telemetryClient?.TrackEvent(...)` / `_telemetryClient?.GetMetric(...)` calls. **All `_logger.Log*` structured logging retained. All public method signatures unchanged.** |

Nothing else touched. The OTel pipeline (`Infrastructure/DI/TelemetryModule.cs`) and the exporter wiring (`Program.cs` `UseAzureMonitor()` + `AzureMonitorGuard`) are unmodified.

## Why this is gap-free (the key finding)

The classic `Microsoft.ApplicationInsights.AspNetCore` package supplies `TelemetryClient` and `AddApplicationInsightsTelemetry()`. **Neither was wired:**

- `AddApplicationInsightsTelemetry()` is **never called** — grep across `src/server` finds it only in a csproj comment. It was already replaced by `builder.Services.AddOpenTelemetry().UseAzureMonitor()` (Program.cs:35) in `spaarke-redis-cache-remediation-r1 R7-S7` (2026-06-26).
- `TelemetryClient` is **registered nowhere** — grep across `src/server` finds it only inside `AgentTelemetry.cs`.

`AgentTelemetry` is `AddSingleton<AgentTelemetry>()` (AgentModule.cs:60) with `AgentTelemetry(ILogger<AgentTelemetry> logger, TelemetryClient? telemetryClient = null)`. Because `TelemetryClient` is unregistered and the parameter has a **default value**, the built-in DI container binds `null` (this is also why task 020's `DiGraphValidationTests` DI guard passed clean — the dep is not "unresolvable"). Therefore `_telemetryClient` was **always null at runtime**, and every `_telemetryClient?.TrackEvent(...)` / `?.GetMetric(...)` call has been an **inert no-op since R7-S7**.

**Consequence**: removing the classic path drops **no signal that was flowing**. AgentTelemetry's custom events/metrics (`AgentInteraction`, `AgentPlaybookInvocation`, `Agent.*.Duration/Count`, etc.) have not reached App Insights since 2026-06-26 — this task simply deletes the dead promise. The live signal from AgentTelemetry — its `ILogger` structured logs — is unaffected and continues to export via `UseAzureMonitor()`'s OTel logging.

## Live OTel signals — confirmed preserved (the escalation guard)

`TelemetryModule.AddTelemetryModule()` is unchanged. The custom Meters + Redis instrumentation that DO flow via OTel → Azure Monitor are all intact:

**Custom Meters (`WithMetrics` → `AddMeter`)** — 10 registrations:
`Sprk.Bff.Api.Ai`, `Sprk.Bff.Api.Rag`, `Sprk.Bff.Api.Cache`, `Sprk.Bff.Api.CircuitBreaker`, `Sprk.Bff.Api.Finance`, `PromptShieldTelemetry.MeterName`, `Sprk.Bff.Api.AiCapabilities`, `AiLatencyTelemetry.MeterName`, `InsightWidgetsTelemetry.MeterName`, `EventRulesTelemetry.MeterName`.

**Tracing (`WithTracing` → `AddSource` / instrumentation)**: `Sprk.Bff.Api.Ai`, `Sprk.Bff.Api.Rag`, `Sprk.Bff.Api.Finance`, `InsightWidgetsTelemetry.MeterName` sources + **`AddRedisInstrumentation()`** (StackExchange.Redis dependency spans; no-op under `NullConnectionMultiplexer` dev-fallback per ADR-032).

> **Count note**: the Program.cs comment + task text say "12 Meters"; the current code registers **10** custom `AddMeter` calls (the nominal "12" is historical — meters have been added/consolidated across projects). The material point for FR-06 holds: **every currently-registered custom Meter + the Redis instrumentation flows via OTel and none was touched by this task.** `UseAzureMonitor()` additionally auto-registers ASP.NET Core + HttpClient default instrumentation meters.

## Acceptance criteria (FR-06) — all met

- ✅ No `Microsoft.ApplicationInsights.AspNetCore` reference or classic-SDK wiring remains (grep: only doc-comments in AgentTelemetry.cs). BFF builds green (0 errors).
- ✅ The custom Meters + Redis instrumentation are confirmed registered in the OTel pipeline (TelemetryModule.cs, unchanged).
- ✅ Carve-out documented here for the PR ADR Tensions table (intentional NFR-01 telemetry change).
- ✅ Negative: telemetry-only — `AgentTelemetry`'s public methods (`TrackAgentInteraction`, `TrackPlaybookInvocation`, `TrackHandoff`, `TrackError`, `TrackSessionDuration`, `StartTimer`, `InteractionTypes`) are byte-for-byte signature-identical; no other behavior rides along.

## §10 BFF publish-size

This task **removes** a package (net reduction in publish surface, per ADR-029 / spec FR-12). No ceiling risk. Formal compressed re-measurement is **task 031** (which re-baselines the whole net10 publish); this task's delta is strictly negative.

## Quality gates (Step 9.5)

- **code-review**: PASS — mechanical removal of dead (always-null-guarded) classic-SDK calls + one package ref; all public signatures preserved; live `ILogger` path retained; no logic altered. No new HIGH CVE (package removal). ADR-015 no-content-logging discipline preserved (retained logs emit only identifiers/timings/outcomes).
- **adr-check**: PASS — ADR-010 (AgentTelemetry stays concrete, single AddSingleton — unchanged); ADR-015 (no content logged — unchanged); ADR-029 (publish surface reduced). No §10 BFF *addition* (this is a removal → Placement Justification N/A). No §6.5 conflict.

## Optional future cleanup (NOT done here — deploy-config, out of tight scope)

- `appsettings.Production.json.template` has an inert `Logging:ApplicationInsights:LogLevel` section (the classic ILogger-provider filter). With the classic provider gone it is ignored by the config system (harmless). Left as-is to avoid deploy-config churn in a zero-behavior-change retarget; a future hygiene pass may drop it.
- If the team wants the agent-gateway custom metrics (interaction/playbook/handoff/error/session) as **OTel** metrics, that is a **new** feature (they have been dark since R7-S7) — file it separately; reviving them here would exceed the FR-06 "remove classic SDK" scope + the telemetry-only acceptance criterion.

## Live validation (deferred to task 051)

This task proves the **wiring** (build-green, OTel sole path, meters registered). Proving the **signal** — requests/dependencies/exceptions/the custom Meters/Redis spans actually appearing in App Insights — happens on the `spaarke-bff-dev` slot smoke in **task 051** (behavioral telemetry is best proven, not reasoned), alongside the re-scrape's ActivitySource-sampling / W3C-propagator trace-continuity check.
