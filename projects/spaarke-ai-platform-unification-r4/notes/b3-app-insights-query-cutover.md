# B-3 Telemetry Constant Rename — App Insights Query Cutover Memo

> **Date**: 2026-05-26
> **Driver**: R4 task 062 (B-3 / FR-06)
> **Status**: Code change shipped (this commit) — App Insights query migration pending operator action

---

## What changed (code)

| Aspect | Before | After |
|---|---|---|
| TypeScript identifier | `TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE` | `TELEMETRY_HISTORY_LOAD_FAILURE` |
| Emitted event name (App Insights `customEvents.name`) | `spaarke-ai-error.history-overlay.load-failure` | `spaarke-ai-error.history.load-failure` |

**Why**: The "overlay" suffix was vestigial — the event is emitted any time the chat-session history list fetch fails (whether the overlay is open or not). Per R4 FR-06 / B-3, rename clarifies the event's actual scope.

---

## What the operator must do (App Insights side)

R4 introduces NO new App Insights queries; this is a cutover for existing ones.

### 1. Audit existing queries / alerts

Run the following KQL in the App Insights workspace to find any queries / alerts / workbooks referencing the old name:

```kql
customEvents
| where name == "spaarke-ai-error.history-overlay.load-failure"
| where timestamp > ago(30d)
| count
```

If non-zero count → there are existing telemetry events under the old name.

### 2. Search for saved queries + alerts referring to the old name

In App Insights workspace UI:
- **Logs > Queries**: search for `spaarke-ai-error.history-overlay.load-failure`
- **Alerts > Alert rules**: search same string in alert query bodies
- **Workbooks**: search same string

### 3. Cutover strategy options

| Strategy | Risk | Effort |
|---|---|---|
| **A. Union both names** (recommended for first 30 days) — modify queries to `where name in ("spaarke-ai-error.history-overlay.load-failure", "spaarke-ai-error.history.load-failure")` | Low — captures both old (pre-deploy) and new events; clean migration path | ~10 min per query/alert |
| **B. Replace immediately at deploy** — change queries to `where name == "spaarke-ai-error.history.load-failure"` BEFORE deploy | Medium — gaps in telemetry between pre-deploy queries-updated and post-deploy events-emitted | ~10 min per query/alert |
| **C. Replace immediately after deploy** — change queries AFTER first events arrive under the new name | High — loses any in-flight pre-deploy events; transient alerting gap | ~10 min |

**Recommended**: **Strategy A** for 30 days (data retention window), then drop the OR clause.

### 4. Verification post-deploy

After R4 deploy lands (Phase 7 wrap-up), wait ~5 minutes then run:

```kql
customEvents
| where timestamp > ago(10m)
| where name startswith "spaarke-ai-error.history"
| summarize count() by name
```

Should see events under the NEW name (`spaarke-ai-error.history.load-failure`). If only the old name appears, the deploy didn't update the code — investigate.

---

## Files modified by task 062

- `src/solutions/SpaarkeAi/src/telemetry/errorTelemetry.ts` — constant rename + emitted value change (line 60-61)
- `src/solutions/SpaarkeAi/src/telemetry/__tests__/errorTelemetry.test.ts` — test imports + assertions updated (lines 21, 65, 68-69, 77, 160, 164)
- `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` — import + 2 emit call sites + JSDoc references updated (lines 55, 74, 95, 313, 345)

No App Insights workspace changes required from the code side — operator owns the workspace-side cutover per Strategy A/B/C above.

---

## Cross-references

- R4 spec FR-06: `projects/spaarke-ai-platform-unification-r4/spec.md` (R4 task 062 / B-3 acceptance)
- Telemetry helper origin: R3 task 013 (error telemetry helpers) — see `projects/spaarke-ai-platform-unification-r3/tasks/013-error-telemetry-helpers.poml`
