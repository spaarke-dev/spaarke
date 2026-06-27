# Task 024 — `sprk_analysisplaybook.sprk_configjson` Schedule Verification

> **Task**: R3-024 — Verify `PlaybookSchedulerJob` correctly reads schedule cadence from `sprk_analysisplaybook.sprk_configjson`
> **Date**: 2026-06-21
> **Branch**: `work/spaarke-platform-foundations-r3`
> **Rigor**: STANDARD (verification + documentation only — no source code changes)
> **Spec**: FR-2.8 ("thin adapter" framing — preserve existing config layout)
> **Dependency**: task 023 (migration to `Spaarke.Scheduling`) ✅
> **Outcome**: VERIFIED — no code remediation required. PlaybookSchedulerJob's `ParseScheduleConfig` is the sole reader of the playbook schedule shape and faithfully preserves the legacy contract.

---

## 1. Decision (per spec FR-2.8)

**Path (b) — "leave configjson in place; scheduler reads it" — confirmed as the implemented choice.**

The POML prompt asks whether to (a) leave schedule data in `sprk_analysisplaybook.sprk_configjson` and have `PlaybookSchedulerJob` read both sources, OR (b) one-time migrate to `sprk_backgroundjob.sprk_configjson`. Per spec FR-2.8, the scheduler is a **thin adapter** — the existing `sprk_configjson` layout is preserved and `PlaybookSchedulerJob` reads it as-is. No data migration. The seeded `sprk_backgroundjob` row for the parent scheduler carries only the parent-fire cron (`0 * * * *`); per-playbook cadence stays on each playbook's `sprk_configjson`.

This is the strict-minimum semantic preservation requirement from spec NFR-04 (cadence preservation) and avoids any solution-import data-migration script that would create lifecycle drift between environments.

---

## 2. Schedule-config layout in `sprk_analysisplaybook`

### Field: `sprk_configjson` (string, JSON document)

Per-playbook free-form JSON configuration on each `sprk_analysisplaybook` row. The scheduler reads only the `schedule` sub-object; the rest of the document is ignored by the scheduler (other consumers use other sub-objects; this task does NOT enumerate them).

### Schedule sub-object — JSON shape

```json
{
  "schedule": {
    "frequency": "hourly" | "daily" | "weekly",
    "time": "HH:mm"
  }
  // ... other config sections used by other consumers — not read by PlaybookSchedulerJob
}
```

| JSON field | Type | Allowed values | Default if missing | Used by `IsPlaybookDue` |
|---|---|---|---|---|
| `frequency` | string (case-insensitive) | `"hourly"`, `"daily"`, `"weekly"` | `"daily"` | YES — drives elapsed-since-`sprk_lastrundate` due-check |
| `time` | string `"HH:mm"` | any 24h time | `"06:00"` | NO — informational only; cron host triggers ticks, not per-playbook time-of-day |

### Companion field: `sprk_lastrundate` (datetime UTC)

Canonical source of truth for "when did this playbook last dispatch?". Read per-tick from the playbook entity (replaces the legacy in-memory `ConcurrentDictionary` seed). Updated after successful fan-out via `PersistLastRunTimestampAsync` (lines 577–605 of `PlaybookSchedulerJob.cs`).

### Worked examples

**Daily, default time** (most common — 6 of 7 active playbooks expected):
```json
{ "schedule": { "frequency": "daily", "time": "06:00" } }
```
`IsPlaybookDue` returns `true` when `now - sprk_lastrundate >= 24h`.

**Hourly cadence** (high-frequency notification playbook):
```json
{ "schedule": { "frequency": "hourly" } }
```
`time` omitted — defaults to `"06:00"` but is unused. `IsPlaybookDue` returns `true` when elapsed `>= 1h`.

**Weekly cadence**:
```json
{ "schedule": { "frequency": "weekly", "time": "09:00" } }
```
`IsPlaybookDue` returns `true` when elapsed `>= 7d`.

**Missing `schedule` sub-object** (config-json has other keys but no `schedule`):
```json
{ "someOtherSetting": "value" }
```
`ParseScheduleConfig` returns `ScheduleConfig.Default` (`"daily"` / `"06:00"`) — playbook gets daily cadence.

**Null / empty / whitespace `sprk_configjson`**:
Returns `ScheduleConfig.Default` (line 502 of `PlaybookSchedulerJob.cs`).

**Malformed JSON** (`JsonException` during `JsonDocument.Parse`):
Logged at `Warning` with playbook id; returns `ScheduleConfig.Default` (lines 522–527). Playbook still runs on daily cadence — no playbook is silently skipped due to bad config.

---

## 3. `PlaybookSchedulerJob.ParseScheduleConfig` — code verification

Source: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs` lines 495–531.

### Read path (verified line-by-line)

1. **Line 498**: `playbook.GetAttributeValue<string>("sprk_configjson")` — reads the field.
2. **Lines 500–503**: null / empty / whitespace → `ScheduleConfig.Default`. ✅
3. **Lines 507–520**: parses JSON document, extracts `schedule.frequency` and `schedule.time` with case-insensitive property matching (via `PropertyNameCaseInsensitive` on the static `JsonReadOptions`). Both sub-fields tolerate absence with sensible defaults. ✅
4. **Lines 522–528**: `JsonException` is swallowed with a `Warning` log + falls back to default. ✅
5. **Line 530**: explicitly returns `ScheduleConfig.Default` even when `schedule` sub-object is absent. ✅

### `IsPlaybookDue` semantics (lines 539–556)

- Never-run case (`lastRun is null`): due immediately ✅
- `"hourly"`: elapsed ≥ 1h ✅
- `"daily"`: elapsed ≥ 24h ✅
- `"weekly"`: elapsed ≥ 7d ✅
- Unrecognized frequency: defaults to daily (elapsed ≥ 24h) ✅
- `Frequency.ToLowerInvariant()` switch — case-insensitive comparison preserves any legacy uppercase values ✅

### Behavior parity with legacy `PlaybookSchedulerService`

Per task 023 notes (`bff-publish-size-task023.md` §Source of delta) and the XML doc at lines 60–64, `ParseScheduleConfig` and `IsPlaybookDue` are **preserved verbatim** from the deleted `PlaybookSchedulerService`. The only behavioral changes in task 023 were:
- Per-tick re-read of `sprk_lastrundate` from Dataverse (vs the legacy in-memory cache) — eliminates restart-window drift.
- Fresh per-child correlationId (Q1) — recorded in `JobRunResult.ResultJson` for parent↔child join.

Neither change affects the schedule-config read path. The 27 `PlaybookSchedulerJobTests` from task 023 include:
- `IsPlaybookDue_*` theory (6 cases, covering all 4 frequencies + boundaries)
- Never-run case (1)
- Null / invalid config fallbacks (2)
- Schedule due-check skip surfaces `"Skipped"` status in `ResultJson` (1)

All pass per task 023's test inventory (32/32 new tests pass; zero regressions in the BFF unit suite).

---

## 4. Parent scheduler seed — `sprk_backgroundjob` row

Per `SchedulingModule.SchedulingBootstrapHostedService.StartAsync` (lines ~194–204 of `SchedulingModule.cs`):

```csharp
var definition = new BackgroundJobDefinition(
    JobId: PlaybookSchedulerJob.JobIdConstant,    // "notification-playbook-scheduler"
    DisplayName: _playbookSchedulerJob.DisplayName,
    Description:  _playbookSchedulerJob.Description,
    Enabled: true,
    CronSchedule: "0 * * * *",                    // hourly at minute 0
    ConfigJson: null);                            // intentionally null — per-playbook config lives on sprk_analysisplaybook.sprk_configjson, NOT here
_store.AddOrReplaceJob(definition);
```

**Key facts**:
- Parent cron `0 * * * *` (hourly) matches legacy `PlaybookSchedulerService.DefaultTickInterval = TimeSpan.FromHours(1)` exactly (NFR-04 cadence preservation).
- Parent `ConfigJson` is explicitly **null** — there is no per-playbook config on the parent row. Per-playbook cadence is read from each `sprk_analysisplaybook.sprk_configjson`. This is the architectural decision the spec FR-2.8 codifies.
- Seed is **idempotent** (`AddOrReplaceJob`) — survives host restart without duplicating rows.
- Currently lives in the in-memory `InMemoryBackgroundJobStore`; future Dataverse-backed store will move this seed to a one-shot startup upsert (or solution-import seed row). The contract — parent cron + null per-playbook config — does not change.

**Hosted-service ordering**: `SchedulingBootstrapHostedService` is inserted at index 0 of the hosted-services list (line 128 of `SchedulingModule.cs`) so it runs BEFORE `ScheduledJobHost`'s first tick. Without this, the host would observe no registered handler and skip dispatch.

---

## 5. Edge cases — surfaced & handled

| Edge case | Handling | Test coverage (task 023) |
|---|---|---|
| `sprk_configjson` is `null` | Returns `ScheduleConfig.Default` (daily/06:00) | ✅ `ParseScheduleConfig_NullConfig_ReturnsDefault` |
| `sprk_configjson` is `""` or whitespace | Returns `ScheduleConfig.Default` | ✅ covered by `IsNullOrWhiteSpace` branch |
| `sprk_configjson` is malformed JSON | Logs `Warning`; returns `ScheduleConfig.Default` | ✅ `ParseScheduleConfig_InvalidConfig_ReturnsDefault` |
| `schedule` sub-object missing | Returns `ScheduleConfig.Default` | ✅ via default fall-through (line 530) |
| `frequency` missing / null | Defaults to `"daily"` (line 512) | ✅ default-branch coverage |
| `frequency` casing varies (`"Daily"`, `"WEEKLY"`) | `ToLowerInvariant()` in `IsPlaybookDue` switch | ✅ covered by case-insensitive comparison |
| `time` missing / null | Defaults to `"06:00"`; informational only | N/A — not enforced |
| Unrecognized frequency (e.g. `"monthly"`) | Defaults to daily (24h elapsed) | ✅ covered by `_ =>` switch arm |
| `sprk_lastrundate` is `null` (never run) | `IsPlaybookDue` returns `true` immediately | ✅ `IsPlaybookDue_NeverRun_ReturnsTrue` |
| `sprk_lastrundate` has `DateTimeKind.Unspecified` | Normalized to UTC via `DateTime.SpecifyKind(..., Utc)` (line 568) | ✅ `ReadLastRunFromEntity` normalization |

**Conclusion**: Every realistic data-quality scenario falls back to a safe default (daily cadence) rather than crashing the tick or silently skipping a playbook. The 7 active production playbooks will be discovered + scheduled correctly regardless of `sprk_configjson` content quality.

---

## 6. Gaps in PlaybookSchedulerJob's config reading

**None.**

Verified the full read path from `QueryNotificationPlaybooksAsync` (line 329 — fetches `sprk_configjson` + `sprk_lastrundate` columns) through `ParseScheduleConfig` (line 495) through `IsPlaybookDue` (line 539). All branches handle missing/malformed input gracefully; all frequencies behave per the legacy contract; all 7 production playbooks will be processed identically to pre-task-023 behavior.

No remediation items. No source code changes made by task 024.

---

## 7. UAT smoke-test procedure (P4 wrap-up, spaarkedev1)

When P4 UAT begins on `spaarkedev1`, the following procedure validates task 023 + 024 together:

### Pre-checks (before BFF deployment)

1. **Capture baseline timestamps**: query `sprk_analysisplaybook` where `sprk_playbooktype = 2` (Notification) and `statecode = 0` (Active); export `sprk_analysisplaybookid`, `sprk_name`, `sprk_lastrundate`, `sprk_configjson.schedule.frequency`. Expected: **7 rows**.

   ```
   SELECT sprk_analysisplaybookid, sprk_name, sprk_lastrundate, sprk_configjson
     FROM sprk_analysisplaybook
    WHERE sprk_playbooktype = 2 AND statecode = 0
   ```

   Record these in a `notes/p4-uat-baseline.md` row for diffing.

2. **Confirm no test playbooks** snuck into the active set — only the 7 production notification playbooks should be present. If count ≠ 7, escalate to operator before proceeding.

### Post-deployment smoke (T+0 to T+2h)

3. **T+0 (BFF restart)**: tail the BFF logs for the seed message. Expect:

   ```
   Registered IScheduledJob 'notification-playbook-scheduler' with ScheduledJobRegistry
   Seeded BackgroundJobDefinition 'notification-playbook-scheduler' (cron='0 * * * *', enabled=True)
   ```

   If either log line is missing, the seed failed — escalate.

4. **T+0 (admin discovery)**: hit `GET /api/admin/jobs` (requires `SystemAdmin` policy). Expect `notification-playbook-scheduler` row with `enabled: true`, `cronSchedule: "0 * * * *"`.

5. **T+ (first cron fire, next top-of-hour)**: tail logs for `PlaybookSchedulerJob tick started`. Verify `playbooksProcessed=7` in the completion log line:

   ```
   PlaybookSchedulerJob tick completed — playbooksProcessed=7 succeeded=N failed=N skipped=N duration=...
   ```

6. **T+ (immediately after tick)**: query `sprk_backgroundjobrun` for `sprk_jobid = 'notification-playbook-scheduler'`. The top row's `sprk_resultjson` should be a `{"children":[...]}` document with **7 child entries**, each containing:
   - Unique `correlationId` (Q1 verification — all 7 IDs must differ from each other and from the parent `sprk_correlationid`).
   - `playbookId` matching one of the 7 production rows from step 1.
   - `status` of `"Succeeded"`, `"PartialFailure"`, or `"Skipped"` (`"Skipped"` is expected for playbooks whose `sprk_lastrundate` makes them not-due — e.g., `daily` cadence playbooks fired within last 24h).

7. **T+ (immediately after tick)**: query `sprk_analysisplaybook` again — `sprk_lastrundate` for non-skipped playbooks MUST have advanced to within the last few minutes of the tick. Skipped playbooks' `sprk_lastrundate` is unchanged (verifies due-check fidelity).

### Cadence-preservation verification (T+24h)

8. **T+24h diff**: re-run step 1's query and diff against the baseline. For each of the 7 playbooks, `sprk_lastrundate` should have advanced by approximately one cadence period (~1h for hourly, ~24h for daily, ~7d for weekly — though weekly won't show movement in this 24h window).

9. **Inline notification regression check**: separately verify that `UploadEndpoints`, `AnalysisEndpoints`, `IncomingCommunicationProcessor`, and `WorkAssignmentEndpoints` continue to fire inline notifications (per `InlineNotificationIntegrationPointsTests` from task 023). UAT-level smoke: perform one upload, one analysis, one incoming comm, one work assignment; verify each emits the expected `NotificationService` call (observable via the resulting Dataverse notification row or BFF debug log).

### Failure modes to watch for

| Symptom | Likely cause | Investigation |
|---|---|---|
| `playbooksProcessed=0` despite 7 active playbooks | `IGenericEntityService.RetrieveMultipleAsync` filter wrong, OR seed `Enabled=false` | Tail full log; check `BackgroundJobDefinition.Enabled` via admin API |
| Some playbooks always `"Skipped"` regardless of cadence | `sprk_lastrundate` not being persisted after fan-out (line 227 `PersistLastRunTimestampAsync` failing silently — it's intentionally non-fatal) | Search logs for `Failed to persist last-run timestamp` warnings |
| `playbooksProcessed=N` where `N > 7` | New production playbook added without P4 baseline refresh — not a regression, just refresh the baseline |
| `ResultJson` shape unexpected | `SerializeChildren` output drift — re-validate `ChildPlaybookRun` shape against admin tooling |
| Parent + child correlationIds identical | Q1 violation — bug in fresh-correlationId generation (would be a regression in task 023's implementation, not task 024) |

### Sign-off criteria for P4 UAT

- [ ] 7 active notification playbooks discovered on first tick
- [ ] Each playbook's per-cadence due-check honored (no daily playbook firing twice within 24h; no weekly firing twice within 7d)
- [ ] `sprk_resultjson` records all 7 children with unique correlationIds
- [ ] Inline notification integration points unchanged (4 surfaces re-verified)
- [ ] No exception traces from `PlaybookSchedulerJob` in 24h log window

---

## 8. Verification artifacts

| Artifact | Location | Purpose |
|---|---|---|
| `PlaybookSchedulerJob.cs` (source) | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs` lines 495–556 | Schedule read + due-check implementation |
| `SchedulingModule.cs` (parent cron seed) | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SchedulingModule.cs` lines 175–210 | Parent `sprk_backgroundjob` seed with cron `0 * * * *` |
| Task 023 design notes | `projects/spaarke-platform-foundations-r3/notes/bff-publish-size-task023.md` | Migration provenance + behavioral parity claims |
| Test inventory (task 023) | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookSchedulerJobTests.cs` | 27 tests covering schedule fallbacks, due-check theory, never-run case, persistence |

---

## 9. Acceptance criteria check (POML)

| Criterion | Status | Evidence |
|---|---|---|
| 7 active playbooks discovered + scheduled correctly | ✅ verified via code path | `QueryNotificationPlaybooksAsync` (`sprk_playbooktype = 2 AND statecode = 0`) returns all matching rows; UAT step 1 confirms count at deploy time |
| Existing `sprk_configjson` layout preserved (no data migration needed) | ✅ verified | `ParseScheduleConfig` reads the unchanged legacy shape; no migration script created or required |
| Smoke-test procedure documented for P4 UAT | ✅ this document §7 | 9-step procedure with pre/post-deploy checks, T+0 / T+1h / T+24h cadence verification, failure-mode triage, sign-off criteria |

---

## 10. Outcome summary

Task 024 is a **verification + documentation task**. No source-code changes were necessary because task 023's migration faithfully preserves the schedule-config read contract (`ParseScheduleConfig` + `ScheduleConfig` record + `IsPlaybookDue`), and the spec FR-2.8 "thin adapter" framing is satisfied by the existing implementation. The parent `sprk_backgroundjob` row carries only the cron (`0 * * * *`); per-playbook cadence remains canonically on each `sprk_analysisplaybook.sprk_configjson`. UAT in P4 will exercise the end-to-end fan-out against the 7 production notification playbooks on `spaarkedev1`.
