# Task 205 — a row decision closes its items in batches (DI-146, #2650)

Branch `fix/ontology-205-decision-commit-batching` from `origin/master` `b76078668c` (worktree `C:\wt205`).
Rigor FULL; model opus @ high; steps directional.

## Per-item Dataverse calls on `origin/master` (counted on the commit route's test fakes)

Counted with `DecisionCommitServiceTests.T205_*` (`WireCalls`): the caller-identity client, the BFF's app-only
service (entity-set name excluded: the shipped service keeps it 15 min), the writer's SDK seam and the caller-rights
probe. A Decide row on one matter, every item's subject that matter, Path B review (executors are fakes).

| Seam | per extra item, before | what it was | per extra item, after |
|---|---|---|---|
| as the caller | 2 | (1a) GET the Signal; GET the core matter | 1 (GET the Signal) |
| app-only service | 1 | (1a) catalog row of the core type | 0 |
| writer | 3 | (1c) Retrieve; step 6 Retrieve; step 6 row-version Update | 1 (Update) |
| rights probe | 0 | (one subject: already asked once per distinct subject) | 0 |
| **total** | **6.0** (2 items 20, 18 items 116) | | **2.0** (2 items 16, 18 items 48) |

Hidden from that count (the rig mocks `DecisionCommitFollowUps.SecureFamiliesAsync`): the two-wall check's
`PartitionsOfSignalsAsync` read each Signal with its own `Retrieve` (1 per item); now one paged query
(`SignalSuppressionTests.Task205_*`). A row whose items each have their own subject adds one Write question per
subject (rights are per record and never borrowed): 2 items 18, 18 items 66, **3.0 per extra item** (caller Signal GET,
row-version Update, that subject's Write question), pinned by `T205_ARowWhoseItemsEachHaveTheirOwnSubject_*`.

## What changed

- `DecisionCommitService`: (1c), the replay repair and step 6 read the row's Signals in pages of 100
  (`ReadSignalsAsync`, the `ResolvedSignalReadPage` paging) on the writer connection, same columns and row version.
  Step 6, the finishing path and the replay repair share `CloseAllAsync`: one paged read after the record, then up to
  4 closes at once (`CloseParallelism`), results in the review's order. Each close keeps its own row-version update;
  the first attempt starts from the paged row, a version mismatch re-reads that row by id and decides again, a row
  the page did not return (or a faulted page) is read on its own.
- `SignalCoreRecordAccess.AuthorizeAsync`: optional per-request `CoreReadMemo` (commit route only). Keeps the catalog
  row's table per core type and each core record (table, id) the caller's read ALLOWED; denials and faults are
  never kept. Every Signal row is still read as the caller.
- `OntologyWriterDataverseClient.RetrieveMultipleAsync`: the multi-row form of its existing read (so the rows the
  route closes are still read by the identity that closes them).
- `SignalSuppressionService.PartitionsOfSignalsAsync`: one paged query; a Signal the read does not return still
  throws (fail closed).

## Close strategy: bounded parallelism (4), not ExecuteMultiple

ExecuteMultiple would need a new batch seam on the writer, per-response fault mapping and a second pass for the
version-mismatch retry (re-read, decide again), and every existing test answers the close through the single
`UpdateRequest` seam. Bounded parallelism keeps `CloseSignalAsync` (outcome codes, D-120 stamping, the
re-read-on-mismatch rule) byte for byte; it cuts wall time, not calls. The writer connection is already a
singleton shared by concurrent requests.

## Deviations

- The test rig gained one setup (the writer's `RetrieveMultipleAsync` for `sprk_signal`, answered like Dataverse);
  no existing test body changed.
- A first version read the rows through the BFF's app-only service; it broke the unmodified
  `TheRecordLookup_IsByThisCallerAndThisReviewIdInSprkSteps` (which scripts that service's queries). Moved to the
  writer connection, which is also the identity the route read Signals with before.

## Verification

- Fail before: `T205_ARowDecision_CostsAtMostThreeDataverseCallsPerExtraItem_*` (6.0 per extra item on `origin/master`),
  `T205_AnItemChangedAfterTheRowWasRead_*` (9 Retrieves on `origin/master`) and `SignalSuppressionTests.Task205_*`
  (0 queries on `origin/master`) fail on the old code and pass now. The isolation tests (another matter's read is never
  reused; an unreadable item is denied; a write denial on one subject) pass on both: regression guards.
- Review round 1 (adversarial, one pass): no runtime defect, no isolation regression. One fix-now test gap (F2): the
  paged read's fault paths were untested. Fixed with a `SignalPageFaults` rig knob and three tests (fault before the
  record gives `Unavailable`/`SignalUnreadable` and writes nothing; fault after the record falls back to a read by id per
  item and closes all; fault on a replay reports `signal_close_not_checked` and leaves the item open). A mutation that
  drops the per-id fallback fails the second one. Known limits kept: closes do not run concurrently on the synchronous
  fakes (K3; outcomes are by index); the read-then-close window is a little wider (K2; a row reopened in that window is
  reported `closed_during_review`, a closed one still meets the row-version check).
- `Sprk.Bff.Api.Tests` filtered (`DecisionCommit|Signals|Mcp|DecisionCapability|DecisionPlan|OntologyWriter`):
  5570 passed, 0 failed, 7 skipped. `Spaarke.ArchTests` full: 2783 passed, 0 failed. BFF build with
  `-warnaserror`: 0 warnings. No package changes; `dotnet list package --vulnerable --include-transitive`: none.

## Publish size (CLAUDE.md section 10)

`dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` in fresh worktrees, zipped with
`Compress-Archive -CompressionLevel Optimal` (PowerShell 7) over `deploy/api-publish/*`, PDBs included.

| | files | zip bytes | MB |
|---|---|---|---|
| `origin/master` `b76078668c` | 194 | 43,562,058 | 41.544 |
| branch | 194 | 43,563,687 | 41.546 |
| **delta** | 0 | **+1,629** | **+0.002** |

Uncompressed: `Sprk.Bff.Api.dll` +5,632 bytes, its PDB +852. Total 41.5 MB, under the 55 MB review line.

rework: fix-round-1 task 205 (review F2: paged-read fault tests)

## Findings outside scope (for the main session to file)

- `CloseSignalAsync`'s `signal_changed` outcome is unreachable: a second version mismatch is caught by the general
  catch (`attempt == 1`) and reported as `signal_close_failed`. Behaviour kept as is (pinned by
  `T205_OneItemsRowVersionKeepsChanging_*`); the code and the `signal_changed` text disagree.
- The route ledger entry for `POST /api/v1/signals/{signalId:guid}/decision` (uac-r2's
  `RouteAuthorizationGuardTests.Ledger.cs`) says each Signal "and its core record" is read as the caller. Still true
  per record; the commit now asks the caller's Read on a shared core record once per request. uac-r2 should hear
  of the memo (#1355).
