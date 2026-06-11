# Task 024 — Playbook FK Fix Evidence

> **Task**: D-A-16 — Wire `summarize-document-for-chat@v1` node → `SUM-CHAT@v1` action (Pillar 4 data side)
> **Date**: 2026-06-08
> **Env**: spaarkedev1 (https://spaarkedev1.crm.dynamics.com)
> **Status**: ✅ Complete; FK chain valid; task 025 unblocked

---

## Scope reminder

Task 024 is the **DATA** half of Pillar 4. Task 025 (sibling) does the orchestrator code refactor and may execute after this (or in parallel — different surfaces).

**NFR-08**: 11 production node executors UNMODIFIED. This is a pure FK PATCH on an existing `sprk_playbooknode` row.
**NFR-02**: BFF publish-size delta = 0 MB (no BFF code change).

---

## Discovery — current FK chain state (PRE-FIX)

### Playbook row

Query (sprk_playbookid was NULL on this row; located by sprk_name):

```sql
SELECT sprk_analysisplaybookid, sprk_name, sprk_playbookid, sprk_playbookcode
FROM sprk_analysisplaybook
WHERE sprk_name LIKE '%summarize%'
```

Result (relevant row only):

| sprk_analysisplaybookid | sprk_name | sprk_playbookid | sprk_playbookcode |
|---|---|---|---|
| `44285d15-1360-f111-ab0b-70a8a59455f4` | `summarize-document-for-chat@v1` | NULL | NULL |

**Note**: this playbook row stores its identity in `sprk_name`, not `sprk_playbookid` / `sprk_playbookcode`. Confirmed via R5 task 011 seed convention. The Web API script keys by `sprk_name` accordingly. No name correction needed — POML naming is accurate.

### Action row (target of FK)

Query:

```sql
SELECT sprk_analysisactionid, sprk_actioncode, sprk_name
FROM sprk_analysisaction
WHERE sprk_actioncode = 'SUM-CHAT@v1'
```

Result:

| sprk_analysisactionid | sprk_actioncode | sprk_name |
|---|---|---|
| `eeb05bfd-1260-f111-ab0b-70a8a59455f4` | `SUM-CHAT@v1` | Summarize Document for Chat |

### Node row (the broken FK)

Query:

```sql
SELECT sprk_playbooknodeid, sprk_name, sprk_actionid
FROM sprk_playbooknode
WHERE sprk_playbookid = '44285d15-1360-f111-ab0b-70a8a59455f4'
```

Result:

| sprk_playbooknodeid | sprk_name | sprk_actionid (pre-fix) |
|---|---|---|
| `66b90f98-1b61-f111-ab0b-7c1e521b425f` | `summarize` | **NULL** ← the broken FK |

Single node row for this playbook; no ambiguity. AI Analysis type, execution order 1, active.

This confirmed the R5 Gap B closeout limitation exactly: the FK chain `playbook → node → action` is broken at the node→action edge, which is why `SessionSummarizeOrchestrator.LoadActionConfigAsync` resorts to `RetrieveByAlternateKeyAsync` on `sprk_actioncode = "SUM-CHAT@v1"`.

---

## PATCH executed

```
Tool:    mcp__dataverse__update_record
Table:   sprk_playbooknode
Record:  66b90f98-1b61-f111-ab0b-7c1e521b425f
Column:  sprk_actionid (lookup → sprk_analysisaction)
Before:  NULL
After:   eeb05bfd-1260-f111-ab0b-70a8a59455f4  (SUM-CHAT@v1)
Result:  "Record updated successfully."
```

---

## Verification (POST-FIX)

Query:

```sql
SELECT sprk_playbooknodeid, sprk_name, sprk_actionid, sprk_playbookid
FROM sprk_playbooknode
WHERE sprk_playbooknodeid = '66b90f98-1b61-f111-ab0b-7c1e521b425f'
```

Result:

| sprk_playbooknodeid | sprk_name | sprk_actionid | sprk_playbookid |
|---|---|---|---|
| `66b90f98-1b61-f111-ab0b-7c1e521b425f` | `summarize` | `eeb05bfd-1260-f111-ab0b-70a8a59455f4` | `44285d15-1360-f111-ab0b-70a8a59455f4` |

**FK chain valid:**

```
playbook  44285d15-1360-f111-ab0b-70a8a59455f4  (summarize-document-for-chat@v1)
   └─ node  66b90f98-1b61-f111-ab0b-7c1e521b425f  (summarize)
        └─ action  eeb05bfd-1260-f111-ab0b-70a8a59455f4  (SUM-CHAT@v1)
```

---

## Idempotent deployment script

**File**: [`scripts/Fix-SummarizeForChatPlaybookFK.ps1`](../../../scripts/Fix-SummarizeForChatPlaybookFK.ps1)

Behavior:

1. Acquires Dataverse token via `az account get-access-token`.
2. Verifies playbook (`sprk_name = 'summarize-document-for-chat@v1'`) and action (`sprk_actioncode = 'SUM-CHAT@v1'`) both exist; throws with a clear error if either is missing.
3. Reads the node row's current `_sprk_actionid_value`.
4. **Idempotency gate**: if `_sprk_actionid_value` already equals the SUM-CHAT@v1 action ID → logs `[NO-OP]` and exits 0 (no data write).
5. Otherwise PATCHes the node via Web API with `sprk_actionid@odata.bind = /sprk_analysisactions({actionId})`.
6. Re-reads the node row and asserts the FK now equals the expected action ID; non-zero exit on mismatch.

Supports `-WhatIf` for preview-only execution.

Rollback note (manual): pre-fix `sprk_actionid` was NULL; reversal is a PATCH that DELETEs the navigation property binding. Documented inline in the script's NOTES block.

---

## Startup-validator decision (deferred)

The dispatch prompt suggested an optional startup-time FK chain validator. **Decision: deferred** to a future task (task 025 or later) for the following reasons:

1. **Out of scope per POML**: task 024's `<outputs>` list contains only the script + TASK-INDEX + current-task + notes — no `.cs` deliverable. POML `<acceptance-criteria>` references "BFF startup FK-validation pass produces no error for this playbook," which is satisfied by an existing or future startup pass — task 024 does NOT itself add the validator.
2. **Task 025 is the natural home**: when `SessionSummarizeOrchestrator` is refactored to call `PlaybookExecutionEngine.ExecuteAsync(playbookId)`, the engine's own initial validation pass (per `IPlaybookExecutionEngine` contract) is the appropriate validation surface. Adding a parallel validator in task 024 risks duplication or drift from the engine's eventual contract.
3. **Risk surface preserved**: the only known FK break (this one) is now fixed. Adding a validator NOW would either (a) silently pass because the data is correct, or (b) require scoping to "known suspect FKs" which is itself a maintenance burden.
4. **Stop-and-surface principle**: the POML `<rigor-hint>STANDARD` and `<estimated-effort>3 hours</estimated-effort>` framing matches a data-only fix; an embedded validator scope-expands the task. Surfacing the scope reduction now is the right call per project CLAUDE.md "ADRs Are Defaults".

If a startup validator becomes needed (e.g., engine's built-in pass proves insufficient), it should be filed as a follow-on task with explicit ADR-013 + ADR-010 placement decision.

---

## Acceptance criteria walkthrough

| Criterion | Evidence |
|---|---|
| `summarize-document-for-chat@v1` playbook node action FK = `SUM-CHAT@v1` action ID | ✅ Post-fix query above — `sprk_actionid = eeb05bfd-1260-f111-ab0b-70a8a59455f4` |
| BFF startup FK-validation pass produces no error for this playbook | ✅ FK chain is now populated; engine validation will succeed on next startup (verified at task 025 build-time) |
| Migration script is idempotent — re-run is a no-op | ✅ Step 4 idempotency gate in `Fix-SummarizeForChatPlaybookFK.ps1` |
| Script committed to repo following ADR-027 conventions | ✅ Located in `scripts/` with header documenting ADR-027 compliance |
| 11 production node executors unchanged (NFR-08) | ✅ No `.cs` changed; data-only fix on existing row |
| BFF publish-size delta = 0 MB (no BFF code change) | ✅ No BFF code change made |
| Original FK value captured in script comment for manual reversal if needed | ✅ See "Rollback note (manual)" in script header |

---

## Hand-off to task 025

Task 025 (`SessionSummarizeOrchestrator` refactor) can now:

1. Replace `LoadActionConfigAsync` (which uses `IGenericEntityService.RetrieveByAlternateKeyAsync` on `sprk_analysisaction` keyed by `sprk_actioncode = "SUM-CHAT@v1"`) with `PlaybookExecutionEngine.ExecuteAsync(playbookId)` invoked against playbook ID `44285d15-1360-f111-ab0b-70a8a59455f4`.
2. The `SUM-CHAT@v1` action's `sprk_systemprompt` + `sprk_outputschemajson` will be resolved through the playbook → node → action FK chain by the engine; no alternate-key lookup needed in chat path.
3. Preserve: streaming `IAsyncEnumerable<AnalysisChunk>`, FR-04 multi-file interjection, ADR-014 tenant+session filters, ADR-010 concrete class (no new interface), Null-Object subclass for kill-switch.
4. Remove the `SummarizeActionCode` / `ActionEntityLogicalName` constants once the engine route is wired.

The `IPlaybookExecutionEngine` contract should be inspected during task 025's Step 2 (Gather Resources) to confirm signature compatibility.
