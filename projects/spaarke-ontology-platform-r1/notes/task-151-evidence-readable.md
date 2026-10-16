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
- **Behaviour change, fail closed:** in an environment with no Secure Record owner team, a detail line whose `ref` names no lineage table (or is not `{table}:{id}`) used to keep its detail; it is now withheld, because the caller's read cannot prove it readable.
- **URL size:** 200 ids is about 8 KB of GET URL, under Dataverse's 32 KB limit (the core reads use 100).
- **The exception branch** of the caller read is pinned by `D142_WhenTheCallersWitnessReadThrows_EveryDetailOnThePageIsWithheld_AndTheListIsUnchanged` (review pass 1, K-b, closed).
- **A 403 on a witness table** (no Read on it) withholds only that table's lines and logs nothing; pinned by `D142_WhenTheCallerHasNoReadOnTheWitnessTable_...` (K-a, closed; seeding proof: marking the chunk readable in that branch fails it).
- **A 401 on a witness read** is a fault (every detail withheld, list unchanged, `WitnessReadFailed` logged). `DataverseUserClient.MapErrorCode` maps 401 and 403 to the same `AccessDenied`, so the witness read tells them apart by `StatusCode`; the client's mapping is unchanged. Pinned by `D142_AWitnessRead401_IsAFault_...` (K-c, closed; fails on the pre-fix service).
- **`withheld: true` keeps the `ref`** (table and id) of a witness the caller cannot read. This is the existing secure-wall shape the POML mandates; the id carries no subject or sender.

## Review record

- Two independent adversarial-reviewer launches were refused by the harness (concurrent sub-agent limit, 4 in use by other sessions). The pass was run in-session over `492d00bd1..62431c35f` against the checklist (fail-open paths, ref casing, duplicate refs, both placement branches, 403/fault/unresolved handling, OData shape, other evidence routes, tests that prove nothing, query counts). No F finding; the K items are listed above. Independent pass 1 then ran: CLEAN (no F). Its four items (K-a to K-d) are closed or filed above; independent pass 2 is still owed before merge (see the PR).

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

**Publish size (Release, `Compress-Archive`):** base is a fresh build of `origin/master` @ `f292ad119` (it includes #1111) at `C:\wt151m`; head is `62431c35f` at `C:\wts-151`.

| | Files | Bytes | Zip |
|---|---|---|---|
| Base (master) | 192 | 129,936,932 | 38,904,567 |
| Head | 192 | 129,941,256 | 38,905,808 |
| Delta | 0 | +4,324 | +1,241 |

**CVE:** `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages. No package added.

## Coordination

- uac-r2 files read, not edited: `SecureChildLineage.cs` (the `Children` map), `SecureChildShareSynchronizer.cs` (not touched; the escalation trigger did not fire).
- Tasks 149 and 150 run in parallel. This change touches `SignalReadService.cs` only in `ApplyWitnessRuleAsync`, the new method after it, one constant and doc comments; not `DoLaneMineRules`, `MineFilter`, `ReadSubjectsAsync` (150) or the no-Read refusal log (149).

## Issues filed (D-106)

- **DI-88 [#1900](https://github.com/spaarke-dev/spaarke/issues/1900):** the stored-value residual (above).
- **DI-89 [#1913](https://github.com/spaarke-dev/spaarke/issues/1913):** K-d. A Decide row's `subjectName` is returned without a caller read of the subject. Latent: no policy on spaarkedev1 is Communication-typed and `sprk_communication` has no catalog row (DI-63), so the name is empty today; a communication's primary name is `Email: <subject>`, so it would expose the subject once both exist. Not a one-line change, so filed.
- **DI-80 [#1873](https://github.com/spaarke-dev/spaarke/issues/1873):** read side resolved here; its row is updated.
