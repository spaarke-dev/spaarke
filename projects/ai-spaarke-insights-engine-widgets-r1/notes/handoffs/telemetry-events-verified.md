# Telemetry events verified — task 051 handoff

> **Task**: 051 — Emit `widget.insightcard.invoked` from invocation path
> **Date**: 2026-06-11
> **Rigor**: FULL (POML `<rigor>FULL</rigor>`)
> **Build status**: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors (pre-existing warnings only, unrelated to this change)

---

## What landed

- `src/server/api/Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs` — telemetry hooked into the `POST /api/insights/ask` handler.
  - Added DI parameter `InsightWidgetsTelemetry widgetTelemetry` to `Ask(...)`.
  - Wraps the `IInsightsAi.AnswerQuestionAsync` call in an `Activity` started via `widgetTelemetry.StartActivity("InsightSummaryCard.Invoke", tenantId, subject, correlationId)` — disposed on scope exit.
  - Wraps the call in a `Stopwatch` for end-to-end duration (cache lookup + playbook + serialisation).
  - Calls `widgetTelemetry.RecordInvocation(topic, mode, outcome, cacheHit, durationMs, tenantId)` on every exit path (kill_switched / failed / success+cache_hit / success).

> **Note on file path**: the task POML referenced `Endpoints/InsightsEndpoints.cs`; the actual file is `Api/Insights/InsightEndpoints.cs` (singular, in the `Api/Insights/` subtree). No DI registration change needed — task 050 already registered `InsightWidgetsTelemetry` as a singleton in `Infrastructure/DI/AnalysisServicesModule.cs` (line 38). Zero DI churn for task 051.

## Dimension mapping (NFR-06 + task 050 bounded set)

| Spec NFR-06 tag | Where it lives | Value |
|---|---|---|
| `topic` | Counter + histogram dim | `"matter-health"` (r1 single-topic; `DefaultTopic` const) |
| `mode` | Counter + histogram dim | `"single"` (r1 single-mode; `DefaultMode` const) |
| `subject` | **Activity span tag only** (per task 050 ADR-014/015 cardinality discipline — subject GUID is high-cardinality) | `request.Subject` (e.g., `matter:GUID`) |
| `duration` | Histogram value | `widgetStopwatch.Elapsed.TotalMilliseconds` (end-to-end) |
| `outcome` | Counter + histogram dim | One of `success` / `failed` / `cache_hit` / `kill_switched` |
| `cacheHit` | Counter + histogram dim | `result.CacheHit` (boolean string) |
| (optional) `tenant.id` | Counter + histogram dim | Tenant id from `tid` claim |

### Outcome dim — exit-path mapping

| Exit path | outcome | cacheHit | Why |
|---|---|---|---|
| `FeatureDisabledException` (ADR-018/032 kill-switch) | `kill_switched` | `false` | Distinguishes the 503 path from generic failure for ops dashboards (spec FR-25 acceptance). |
| `Exception` (other than `OperationCanceledException` / `ArgumentException`) | `failed` | `false` | Generic 500 path. |
| `result.CacheHit == true` (success served from cache) | `cache_hit` | `true` | Cache-hit-rate dashboards work without joining cacheHit + outcome dims. |
| `result.CacheHit == false` AND artifact OR decline produced | `success` | `false` | Decline is a successful playbook execution producing a structured result, NOT a failure. |
| `OperationCanceledException` (caller disconnect) | — (no event) | — | Cancellation is not an invocation outcome; propagates to Kestrel. |
| `ArgumentException` (facade validation) | — (no event) | — | 400 validation; playbook never ran. |

This matches the task 050 `InsightWidgetsTelemetry.RecordInvocation` `ValidOutcomes` enum exactly. Out-of-enum input would throw `ArgumentException` from the helper — all four call sites use literal strings from the enum, so cardinality is enforced at compile/runtime.

## ADR + constraint compliance

- ✅ **ADR-013 / DR-003** — endpoint stays in Zone B; consumes `IInsightsAi` only. `InsightWidgetsTelemetry` is BFF infrastructure (not AI internals), and is in `Sprk.Bff.Api.Telemetry` — not under `Services/Ai/*`. No Zone boundary violated.
- ✅ **ADR-014 / ADR-015 cardinality** — bounded dims only on counter/histogram (topic, mode, outcome, cacheHit, tenant.id). Subject GUID, correlationId, and other high-cardinality values are on the Activity span tags only (per task 050 `StartActivity` parameter contract).
- ✅ **ADR-018 / ADR-032** — `FeatureDisabledException` caught BEFORE generic `Exception` catch so 503 takes precedence; `kill_switched` outcome dim distinguishes from generic `failed` so dashboards can track ADR-018 kill-switch frequency vs underlying runtime failure rate.
- ✅ **NFR-09** — no new ADR introduced. No new DI registrations. Re-uses existing `InsightWidgetsTelemetry` singleton from task 050.

## App Insights KQL — verification queries (SC-11)

These are the canonical queries Ops will run once a deployed env has had a couple of widget invocations. (Empirical verification requires deployment to dev App Service + running real invocations — that gate runs in Wave 3 task 066 SC-11 verification, not this commit. Source-level wiring is correct, build is clean, dim contract matches task 050 helper.)

```kql
// 1. Invocation volume by topic
customMetrics
| where name == "widget.insightcard.invoked"
| summarize count() by tostring(customDimensions["topic"])

// 2. Cache-hit rate
customMetrics
| where name == "widget.insightcard.invoked"
| summarize sum(value) by tostring(customDimensions["cacheHit"])

// 3. p95 latency by topic + mode
customMetrics
| where name == "widget.insightcard.duration"
| summarize percentile(value, 95) by tostring(customDimensions["topic"]), tostring(customDimensions["mode"])

// 4. Kill-switch frequency (ADR-018 monitoring)
customMetrics
| where name == "widget.insightcard.invoked"
| where customDimensions["outcome"] == "kill_switched"
| summarize count() by bin(timestamp, 1h)

// 5. Failure rate
customMetrics
| where name == "widget.insightcard.invoked"
| summarize
    total = sum(value),
    failures = sumif(value, customDimensions["outcome"] == "failed"),
    killSwitched = sumif(value, customDimensions["outcome"] == "kill_switched")
    by bin(timestamp, 1h)
| extend failureRate = (failures + killSwitched) * 1.0 / total
```

Acceptance criteria from POML — empirical column requires deployed env:

| Criterion | Source-level state | Empirical state |
|---|---|---|
| Counter increments on each invocation | ✅ `RecordInvocation` called on every non-cancellation/non-validation exit | ⏭ Deferred to task 066 (post-deploy) |
| Duration histogram populated | ✅ Same call records on duration histogram | ⏭ Deferred to task 066 (post-deploy) |
| App Insights KQL query returns events | ✅ Meter `Sprk.Bff.Api.InsightWidgets` already wired into `TelemetryModule.AddTelemetryModule` (line 38) → flows to OTel → Azure Monitor exporter | ⏭ Deferred to task 066 (post-deploy) |

## What this leaves for downstream tasks

- **Task 052 (parallel sibling)** — touches `Services/Ai/Insights/IInsightsPlaybookExecutionCache` impl; orthogonal to this change. No merge conflict.
- **Task 066 SC-11 verification** (Wave 3) — empirical App Insights verification after dev deploy. The KQL queries above are pre-staged for that task.
- **Topic registry-aware dimensions (future)** — once `sprk_aitopicregistry` reads land in the request pipeline (likely r2+), `DefaultTopic` and `DefaultMode` constants get replaced with per-request lookups. The constants are intentionally documented inline so the future replacement site is discoverable.

## Files modified

- `src/server/api/Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs`
  - Added `using System.Diagnostics;`, `using Sprk.Bff.Api.Configuration;`, `using Sprk.Bff.Api.Telemetry;`
  - Added `DefaultTopic` / `DefaultMode` const declarations
  - Added `InsightWidgetsTelemetry widgetTelemetry` DI parameter to `Ask(...)`
  - Wrapped `IInsightsAi.AnswerQuestionAsync` invocation in `using var widgetActivity = widgetTelemetry.StartActivity(...)` + `Stopwatch.StartNew()`
  - Added `catch (FeatureDisabledException ex)` block (records `kill_switched` outcome + returns 503 via shared `AsFeatureDisabled503` helper)
  - Augmented existing generic `catch (Exception ex)` to record `failed` outcome before 500
  - Added post-call `RecordInvocation` for `success` / `cache_hit` outcomes

No other files modified. No DI registration change. No new ADR. No new package.
