# Task 151: evidence detail hidden unless the caller reads the witness (D-142)

> Branch `task/ontology-151-evidence-readable` (worktree `C:\wts-151`), cut from `origin/docs/ontology-platform-design` at
> `492d00bd1`. Rigor **FULL** (data-exposure rule on the Signal read path). uac-r2 reviews the PR (D-123).

## What changed

**The rule.** A stored evidence line keeps `text` / `source` only when BOTH hold:
1. **D-142 (new):** the caller's own (OBO) read returns the witness row.
2. **D-123 (unchanged):** the witness's secure family is known and equal to the Signal's (`KeepsDetail`).

Otherwise the line loses both properties and carries `"withheld": true` (kind, ref, tier, asOf, clause stay), the same shape as a secure-wall withhold.

**Where.**
- `SignalReadService.WitnessesTheCallerReadsAsync` (new, private): one batched caller read per witness table per request, `In` lists of at most `WitnessIdChunk` (200) ids. The entity set and key come from uac-r2's `SecureChildLineage.Children` (a witness ref can only name a lineage child: `WitnessOf`). 403 on a table = none of its witnesses readable. Any other failure (5xx, throttling, unparseable body, exception) = null.
- `SignalReadService.ApplyWitnessRuleAsync`: runs the caller read first; null withholds every detail on the page (the list is unchanged), otherwise passes the readable set on.
- `SignalSuppressionService.WitnessDetailShownOnReadAsync` gains a required `readableByCaller` input. A reference whose witness is not in it is dropped before anything else, so it keeps nothing and is never placed (no app-only read of a row the caller cannot read). This applies to both branches: the placed one and the "no Secure team in this environment" one (which before kept every reference).
- `SignalSuppressionService.WitnessOf`: private → internal, so the caller read keys witnesses exactly as the keep decision does.
- The fault reuses `SuppressionEvents.WitnessReadFailed` (50353), logged at Warning with the table and the error code (or exception type) only.

**Routes that return evidence lines.** Only `GET /api/v1/signals/worklist` (`SignalReadEndpoints` → `SignalReadService`). Checked by grep for `sprk_evidencerefs` / `EvidenceRefs` across `Api/` and `Services/`: the other readers are the writer (`SignalWriter`), the evaluator, and the job (`SignalReevaluationJob`, write-side redaction). `DecisionPlanEndpoints` and `DecisionCommitEndpoints` return no evidence.

**§11 for the new surface:**
- `WitnessesTheCallerReadsAsync`. **Existing:** the core and subject caller reads in the same service. **Extension:** they read core and subject tables, never the rows a Signal's evidence quotes; this applies the same read to the witness rows (no new route, registration or package). **Cost of doing nothing:** a reader sees the subject and sender of a communication they cannot open, contrary to D-142.
- `WitnessIdChunk` (const) and the `readableByCaller` parameter are modifications of existing surface.

## Query counts (route, in-process fake; caller GETs + app-only service calls)

Shape: N Decide Signals on one ordinary matter, each quoting one communication filed under the matter and its own work assignment, Secure team present (the worst case: the placement runs).

| Signals | Before (caller + app-only = total) | After |
|---|---|---|
| 4 | 5 + 10 = 15 | 6 + 10 = 16 |
| 24 | 5 + 10 = 15 | 6 + 10 = 16 |

The only addition is one caller read per witness table (here one: `sprk_communication`). Pinned by `D142_OneCallerReadPerWitnessTablePerRequest_InListsOfAtMost200Ids`: 24 Signals quoting a communication and a document → exactly one `sprk_communications?` and one `sprk_documents?` read; 201 witnesses in one table → two reads of 200 and 1 ids. The unit-level K1 counts (`SignalSuppressionTests`, 7 for 4 and for 24 Signals) are unchanged.

## Known limits

- **Write-time (POML constraint "write-time"):** not changed. Task 145's write-time rule and the job's stored-evidence redaction cannot know future readers, and no rule on existing ownership primitives matches "readable by the reader". The stored `sprk_signal.sprk_evidencerefs` still holds such detail; any Signal reader can read it directly from Dataverse (the column is not field-secured; no repo form or view shows it). Filed as **DI-88 [#1900](https://github.com/spaarke-dev/spaarke/issues/1900)** with options (recommended: store no detail, read it at read time as the caller).
- **The caller read uses `{table}id` as the key** from `SecureChildLineage.Table.IdColumn` (uac-r2: "every Spaarke table, verified live").
- **A caller-identity fault on the witness read** (OBO exchange failure after the caller was resolved) is treated as a fault: every detail withheld, list unchanged, not the single 403.

## Evidence

**Tests (filtered):**
- Route (`SignalReadRoute`, integration): 44 pass, 2 live legs skipped (opt-in). Six new `D142_*` cases: readable → shown; another BU's ordinary matter → withheld; unfiled privately owned (no Secure team) → withheld; secure family readable by the caller → still withheld; caller-read fault → every detail withheld, list unchanged; one read per witness table, In lists of at most 200.
- Unit (`SignalSuppression`): 105 pass. The read-side cases now pass the readable set (every witness readable) and keep judging the secure-family rule alone.

**Failed before / pass after:** with the pre-change `SignalReadService.cs` and `SignalSuppressionService.cs` and the new tests, 5 of the 6 `D142_*` cases fail (the "readable → shown" case passes before, as it should). All pass after.

**Seeding proof (mutations, each restored):**

| Mutation | Fails |
|---|---|
| M1: `readableByCaller` ignored (every reference judged) | the other-BU and the unfiled cases |
| M2: a failed table read skipped instead of null | the fault case (a readable document witness on the page kept its detail) |

## Coordination

- uac-r2 files read, not edited: `SecureChildLineage.cs` (the `Children` map), `SecureChildShareSynchronizer.cs` (not touched; the escalation trigger did not fire).
- Tasks 149 and 150 run in parallel. This change touches `SignalReadService.cs` only in `ApplyWitnessRuleAsync`, the new method after it, one constant and doc comments; not `DoLaneMineRules`, `MineFilter`, `ReadSubjectsAsync` (150) or the no-Read refusal log (149).

## Issues filed (D-106)

- **DI-88 [#1900](https://github.com/spaarke-dev/spaarke/issues/1900):** the stored-value residual (above).
- **DI-80 [#1873](https://github.com/spaarke-dev/spaarke/issues/1873):** read side resolved here; its row is updated.
