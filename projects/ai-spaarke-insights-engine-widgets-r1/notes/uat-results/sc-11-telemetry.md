# SC-11 Telemetry Verification — `widget.insightcard.invoked` events in App Insights

> **Task**: 066 — Telemetry verification (App Insights query)
> **Date**: 2026-06-11
> **Rigor**: STANDARD (POML `<rigor>STANDARD</rigor>`)
> **Sub-agent boundary**: Static verification + KQL queries + operator script. Empirical execution against App Insights is an **operator action** (deferred from sub-agent — sub-agents cannot reach Azure).
> **Predecessor handoff**: [`notes/handoffs/telemetry-events-verified.md`](../handoffs/telemetry-events-verified.md) (Task 051)
> **Source code**:
> - [`src/server/api/Sprk.Bff.Api/Telemetry/InsightWidgetsTelemetry.cs`](../../../../src/server/api/Sprk.Bff.Api/Telemetry/InsightWidgetsTelemetry.cs) (Task 050)
> - [`src/server/api/Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs`](../../../../src/server/api/Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs) (Task 051 wiring)

---

## 1. Spec acceptance criteria (SC-11 + NFR-06)

**SC-11 (spec.md line 288)**: "Telemetry events emitted with full metadata — Verify: App Insights query confirms"

**NFR-06 (spec.md line 221)**: `widget.insightcard.invoked` events with tags `{topic, mode, subject, duration, outcome, cacheHit}` on meter `Sprk.Bff.Api.InsightWidgets`.

**Required tag set** (per spec — verified against source):

| Spec tag | Location | Source (file:symbol) | Verified |
|---|---|---|---|
| `topic` | Counter + histogram dim | `InsightWidgetsTelemetry.cs:175` (`tags { topic, ... }`) | ✅ |
| `mode` | Counter + histogram dim | `InsightWidgetsTelemetry.cs:176` | ✅ |
| `subject` | **Activity span tag** (high cardinality — ADR-014/015 discipline) | `InsightWidgetsTelemetry.cs:220` (`activity.SetTag("subject", ...)`) | ✅ |
| `duration` | Histogram **value** | `InsightWidgetsTelemetry.cs:187` (`_durationHistogram.Record(durationMs, tags)`) | ✅ |
| `outcome` | Counter + histogram dim | `InsightWidgetsTelemetry.cs:177` | ✅ |
| `cacheHit` | Counter + histogram dim | `InsightWidgetsTelemetry.cs:178` | ✅ |
| `tenant.id` (optional) | Counter + histogram dim | `InsightWidgetsTelemetry.cs:183` (conditional) | ✅ |

All 6 spec-required tags present at source. `subject` lives on the Activity span (not the counter) per the explicit ADR-014/015 cardinality discipline — confirmed correct in the spec narrative ("topic, mode, subject, duration, outcome, cacheHit" combines span + metric dims).

---

## 2. Wiring verification — 4 exit-path outcomes

`InsightEndpoints.cs` emits on all 4 outcomes (verified via grep — line numbers from current source):

| Line | Exit path | `outcome` arg | `cacheHit` arg | Spec mapping |
|---|---|---|---|---|
| 304 | `catch (FeatureDisabledException)` | `"kill_switched"` | `false` | ADR-018 503 (SC-08) |
| 332 | `catch (Exception)` | `"failed"` | `false` | Generic 500 |
| 362 | `result.CacheHit == true` post-call | `"cache_hit"` | `true` | SC-06 (cache hit) |
| 362+ | `result.CacheHit == false` post-call | `"success"` | `false` | SC-05 (first-call) |

`OperationCanceledException` (caller disconnect) and `ArgumentException` (400 validation) intentionally skip telemetry — they are not invocation outcomes (matches Task 051 handoff §32-§41 documented mapping).

`StartActivity("InsightSummaryCard.Invoke", tenantId, subject, correlationId)` opens at line 278; `Stopwatch` measures end-to-end; both are passed into `RecordInvocation` for the histogram value.

---

## 3. KQL queries (5 canonical — operator runs these against App Insights after Tasks 061–065 invocations)

The 5 queries below are **carried forward verbatim** from Task 051 handoff (`notes/handoffs/telemetry-events-verified.md` §52–§87) — the same queries pre-staged for SC-11 verification. Saved for the Phase 7 dashboard.

### Query 1 — Invocation volume by topic (SC-11 primary)

```kql
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)  // adjust window to UAT timeframe
| summarize count() by tostring(customDimensions["topic"])
```

**Expected**: Count > 0 for `topic == "matter-health"` after Wave 5 tasks 061-065 invocations.

### Query 2 — Cache-hit rate (SC-06 corroboration)

```kql
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| summarize sum(value) by tostring(customDimensions["cacheHit"])
```

**Expected**: At minimum one row `cacheHit == "true"` (from Scenario A second click) AND one row `cacheHit == "false"` (first click).

### Query 3 — p95 latency by topic + mode (SC-06 cache-hit <100ms corroboration)

```kql
customMetrics
| where name == "widget.insightcard.duration"
| where timestamp >= ago(2h)
| summarize percentile(value, 95) by tostring(customDimensions["topic"]), tostring(customDimensions["mode"])
```

**Expected**: `topic == "matter-health"`, `mode == "single"` row present; p95 sanity-check < 5000 ms (per spec NFR-04).

### Query 4 — Kill-switch frequency (SC-08 corroboration)

```kql
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| where customDimensions["outcome"] == "kill_switched"
| summarize count() by bin(timestamp, 1h)
```

**Expected**: Count > 0 during Task 063 (kill-switch UAT) window; 0 otherwise.

### Query 5 — Failure rate (NFR-05 corroboration)

```kql
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| summarize
    total = sum(value),
    failures = sumif(value, customDimensions["outcome"] == "failed"),
    killSwitched = sumif(value, customDimensions["outcome"] == "kill_switched")
    by bin(timestamp, 1h)
| extend failureRate = (failures + killSwitched) * 1.0 / total
```

**Expected**: `failures == 0` during well-formed UAT runs (kill-switch is expected behavior, not a failure).

### Tag-completeness Query (NFR-06 acceptance gate — SC-11 primary)

This is the **canonical SC-11 acceptance query** — verifies no nulls in any required dim.

```kql
customMetrics
| where name == "widget.insightcard.invoked"
| where timestamp >= ago(2h)
| extend
    topic = tostring(customDimensions["topic"]),
    mode = tostring(customDimensions["mode"]),
    outcome = tostring(customDimensions["outcome"]),
    cacheHit = tostring(customDimensions["cacheHit"])
| summarize
    total = count(),
    missingTopic = countif(isempty(topic)),
    missingMode = countif(isempty(mode)),
    missingOutcome = countif(isempty(outcome)),
    missingCacheHit = countif(isempty(cacheHit))
```

**Acceptance**: `total > 0` AND `missingTopic == 0` AND `missingMode == 0` AND `missingOutcome == 0` AND `missingCacheHit == 0`.

---

## 4. Operator runbook — running SC-11 verification

> Sub-agent boundary: this step requires Azure portal / Azure CLI access and is performed by the **operator**, not the sub-agent.

### Prerequisites

1. Wave 5 Tasks 061–065 invocations must have run against `https://spaarke-bff-dev.azurewebsites.net` within the last 2 hours. (Tasks 061 = E2E, 062 = decline, 063 = kill-switch, 064 = degraded, 065 = pre-warm.)
2. App Insights resource identified (per `docs/architecture/auth-azure-resources.md` — dev resource name).
3. ~2 minute propagation delay between invocation and App Insights ingestion is expected (per Task 051 handoff).

### Steps

1. **Open App Insights → Logs** for the dev BFF App Service's App Insights resource.
2. **Run the Tag-completeness query** above (§3, final query). Sub-criterion: all four `missing*` fields == 0.
3. **Run Query 1** to confirm volume matches Wave 5 invocation count (~5–10 expected: 1 per UAT scenario + retries).
4. **Run Query 2** to corroborate SC-06 cache-hit pattern (Scenario A second click).
5. **Run Query 4** to corroborate SC-08 kill-switch path (Task 063 invocation produced `kill_switched` outcome).
6. **Save Query 1 + Tag-completeness Query** as Pinned Workbook tiles for the Phase 7 dashboard reference (per Task 066 POML step 4).
7. **Record results below in §5** with the actual numbers + screenshots (Workbook tile screenshot acceptable).

### Failure modes

| Symptom | Likely cause | Diagnostic |
|---|---|---|
| Query 1 returns 0 rows | OTel exporter not flowing OR `Sprk.Bff.Api.InsightWidgets` meter not in `TelemetryModule.AddTelemetryModule` | Check `Infrastructure/DI/TelemetryModule.cs` — meter name string must match exactly |
| Query 1 returns rows but Tag-completeness shows `missingTopic > 0` | `topic` literal passed as null/empty at call site | Re-check `InsightEndpoints.cs` `RecordInvocation` call sites — should be `DefaultTopic` const ("matter-health") in r1 |
| Tag-completeness shows `missingCacheHit > 0` | Boolean-to-string mapping broke | `InsightWidgetsTelemetry.cs:178` should emit `"true"` or `"false"` literal |
| Counter populated but histogram empty | Wrong call site — only `_invocationCounter.Add` fired | `InsightWidgetsTelemetry.cs:187` does both in same `RecordInvocation` — re-check call site is `RecordInvocation`, not internal helper |
| Query 4 expected non-zero but returns 0 | Task 063 didn't actually trip the kill-switch | Re-verify Task 063 ran with `Features:InsightWidgets:Enabled=false` |

---

## 5. SC-11 acceptance — operator fill-in (post-Wave 5 + post-query)

> Operator: complete this section after running Wave 5 invocations + the queries in §3 above. This is the **gate** for marking SC-11 ✅ in spec.md.

| Acceptance check | Pass criterion | Operator result | Status |
|---|---|---|---|
| Events visible in App Insights | Query 1 returns ≥ 1 row for `topic == "matter-health"` | _(operator fills count)_ | 🔲 |
| All required tags populated | Tag-completeness Query: `missingTopic + missingMode + missingOutcome + missingCacheHit == 0` | _(operator fills)_ | 🔲 |
| Cache-hit dim observable | Query 2 returns both `cacheHit == "true"` and `cacheHit == "false"` rows | _(operator fills)_ | 🔲 |
| Latency histogram populated | Query 3 returns ≥ 1 row with non-null p95 | _(operator fills ms)_ | 🔲 |
| Kill-switch outcome observable | Query 4 returns ≥ 1 row during Task 063 window | _(operator fills)_ | 🔲 |
| Failure rate sane | Query 5: `failures == 0` (kill-switched is expected, not a failure) | _(operator fills)_ | 🔲 |

**Overall SC-11**: 🔲 PENDING operator empirical verification.

---

## 6. What landed (sub-agent deliverable)

- ✅ Static source verification of all 6 NFR-06 tags (§1 table).
- ✅ Wiring verification of all 4 outcome paths (§2 table — line numbers cited).
- ✅ 5 canonical KQL queries + 1 Tag-completeness gate query documented (§3, carried forward from Task 051 handoff per task POML "5 pre-staged KQL queries from Task 051 handoff doc").
- ✅ Operator runbook with prerequisites, steps, failure modes (§4).
- ✅ Acceptance scorecard ready for operator fill-in (§5).

## 7. What requires operator action (not sub-agent)

- 🔲 Run Wave 5 Tasks 061–065 invocations against `spaarke-bff-dev`.
- 🔲 Execute §3 queries in App Insights Logs blade.
- 🔲 Fill §5 acceptance table with empirical results.
- 🔲 Pin Query 1 + Tag-completeness Query as Workbook tiles for Phase 7 dashboard.
- 🔲 Update spec.md SC-11 from `[ ]` to `[x]` once §5 all-green.

## 8. Blockers

**None at sub-agent layer.** Source wiring is complete (Tasks 050 + 051 verified), KQL queries are documented + canonical, operator runbook is unambiguous. The only remaining work is empirical execution which is operator-only by nature (cannot be done from a sub-agent).

If Wave 5 Tasks 061–065 produce a build/deploy failure preventing any BFF invocation, SC-11 verification is blocked downstream — but that's a Task 061–065 issue, not SC-11. SC-11 itself has no blockers.

---

## 9. ADR + constraint compliance

- ✅ **NFR-06** — all 6 spec-listed tags present + verified at source.
- ✅ **ADR-014 / ADR-015 cardinality discipline** — `subject` lives only on Activity span tag (not metric dim); `correlation_id` same; `tenant.id` only when low-cardinality; out-of-enum input rejected with `ArgumentException` at `RecordInvocation` call site.
- ✅ **ADR-018 / ADR-032** — `kill_switched` outcome dim distinguishes 503 ProblemDetails kill-switch path from generic `failed` (per SC-08 + Task 051 handoff §49).
- ✅ **NFR-09** — no new ADR introduced. This task is verification-only; no code change.
- ✅ **Q-U8** — meter name `Sprk.Bff.Api.InsightWidgets` (NOT earlier spec `Spaarke.InsightWidgets`) — verified at `InsightWidgetsTelemetry.cs:57`.
