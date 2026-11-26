# Task 226 — Decision Records tab, record first (#2015, DI-104, D-231)

Branch `fix/ontology-226-dr-tab-record-first` from `origin/master`. Rigor FULL (opus @ high, steps directional).

## Findings (why the tab missed records)

`GET /api/v1/signals/worklist?set=decided` was Signal-first: the configuration's `closedEarlier` Signal query, widened to 90 days
on `sprk_resolvedon`, status in (Acknowledged, Resolved), `sprk_decisionrecord` not null; then the one record each Signal's own
lookup named. Three gaps followed from that shape:

1. **A Signal reviewed twice showed one record.** The commit route writes the Signal's `sprk_decisionrecord` on every review
   (`DecisionCommitService.CloseSignalAsync`), so an inquiry review's record was replaced by the decision's. The earlier record
   still exists and still lists the Signal in its `sprk_steps` `resolvedSignalIds`; nothing read it.
2. **An inquiry review could miss the window.** Send inquiry leaves the Signal Acknowledged with no `sprk_resolvedon`, and the window
   was on `sprk_resolvedon`. The test harness did not model the window, so the existing inquiry test passed against code that
   live Dataverse would not have answered.
3. **Email-gate records (D-139) never appeared.** They resolve no Signal (`resolvedSignalIds: []`), so no Signal row led to them.

## Design (owner decision D-231: listed, no Open)

The Decided set now reads the records first and keeps every existing rule for deciding who sees what.

- **(3b) Record read, as the caller.** One FetchXML on `sprk_decisionrecord`: `sprk_decidedon` last-x-days 90 plus
  `WriterCreatedRecord.FetchXmlFilter` (createdby = the writer, createdonbehalfby null). It is ordered by decided-on desc, then id, and
  paged up to `MaxPages`; past that it is a 503, never a shorter history. A 403 on page 1 is an empty set with a warning, and a 401 or
  a refused later page is a 503, the same as the other caller reads. A record whose `sprk_steps` cannot say which Signals it resolved
  is not listed, with a warning count.
- **Signal-backed records.** The Signals read at (4) are exactly those the records name, through the configuration's `closedEarlier`
  query as a template (its window and status condition taken out) narrowed with `sprk_signalid in (...)` in chunks of `IdChunk`.
  Every later rule still applies (core readability, the Do-lane "mine" read, the witness rule, duplicates). At (10), each record is
  listed once under each of its Signals that survived, carrying that record (`VisibleRow.Decision`). A record none of whose Signals
  survived is not listed. The link is the writer's `resolvedSignalIds`, not the Signal's lookup, which a later review overwrites.
- **Records with no Signal (10b).** Email-gate records only, since fix round 1 (see below). They are kept by
  the record's own stamps:
  - Core pair present: kept only when the caller's own batched read of that core returns it. This reuses `ReadReadableCoresAsync`,
    so a type outside `CoreAncestorResolver.CoreRecordEntities` is dropped (K2).
  - Half pair: never kept.
  - No core: kept only when the caller's own read of the `sprk_regardingcommunication` row returns it (its owner, or a person it is
    shared with; D-35/D-39). A no-core record on anything else is not kept.

  These records go to `WorklistGroup.Records` in their core's Decide group (a new group when it has no item), or to a Not filed group,
  last. They carry `Steps = null` and `FollowOns = []`, because nothing replays them.
- **Contract, additive.** `WorklistGroup.Records` is serialized only when non-null (`JsonIgnore WhenWritingNull`), so every other set's
  body is unchanged. `WorklistDecisionRecord.Summary` (`sprk_name`) now appears on every decision object, in the Closed sets too.
- **Client.** `worklistService` parses `summary` and `records`, and keeps a group that has only records. `decisionRecordsView` builds one
  row per record (`item: null` for a record with no item), breaks ties by record id, and uses the record's summary as the title when
  there is no item. `DecisionRecordsWidget` keys rows by `recordId:signalId`, passes Open only when there is an item, makes the loader
  prefer the clicked row (so the earlier review replays the earlier record), and dedupes the wizard's `visibleList`.

Escalation trigger: not fired. No new column (`sprk_name`, `sprk_corerecordtype`, `sprk_corerecordid` and
`sprk_regardingcommunication` all exist and are written by `DecisionRecordWriter`). No role change, no new route. The extra
visibility over master is limited to:
- earlier reviews of Signals the caller already sees;
- records whose Signal has since changed status;
- records with no Signal, gated by their own core stamp, or by the caller's read of their communication.

## Changed existing assertions (justified)

| Test | Was | Now | Why |
|---|---|---|---|
| `ClosedAndDecidedQueries_SelectTheDecisionRecordLookup_WhateverTheLiveConfigurationVersion` | the decided fetch had `last-x-days 90` and `sprk_decisionrecord not-null` | the template has no resolved-on window, no last-x-hours and no not-null; `DecidedRecordsFetch` holds `sprk_decidedon last-x-days 90` | The window moved to the record (gap 2); the Signal query is only ever run narrowed to the records' Signals. |
| `DecidedSet_IncludesAnInquirySentRecord_...` (renamed `..._AndARecordWhoseSignalIsOpenAgain_ButNotASignalWithNoRecord`) | a Signal that is Open again with a record was not listed | its record DR-00006 is listed | D-231: the tab lists records. A Signal re-raised, or a close that did not land, does not erase the decision that was made. |
| `OnlyTheDecidedQuery_WidensTheStatus_...` (renamed `OnlyTheDecidedQuery_DropsTheStatusAndWindow_...`) | the decided fetch had status in (Ack, Resolved) | the template has no status condition and keeps `statecode eq 0` | Same reason; Closed today and Closed earlier keep their status and window (asserted unchanged). |

The fake (`SignalReadRouteTests.World`) now evaluates the record query's `sprk_decidedon` window, its order, its projection and its
paging. It also links records to Signals through `resolvedSignalIds` (`Resolves`), and adds `GateDecision(...)`.

## Fail-before evidence

The fixed `SignalReadService.cs` and `WorklistDecisionRecord.cs` were swapped for the `origin/master` versions, with the one
`DecidedRecordsFetch` reference in the test file replaced by its literal. Running the decided tests then gave **14 failed, 7 passed**.

- Failing included: `DecidedSet_ASignalReviewedTwice_YieldsTwoRows_EachWithItsOwnRecord`, both gate-listing tests, both fault tests,
  paging, the forged-record test (its fetch assertion), the secure-child test (Bob's second review) and the three changed assertions.
- Passing on master: the must-not-fire controls whose behaviour master already had, plus the two-Signals-one-record test.

The files were restored afterwards, and no stash was used. Client side, with master's three client files the DecisionRecords suite
gave **2 failed, 16 passed**: the reviewed-twice test and the gate-row test.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- IntegrationTests `--filter FullyQualifiedName~Signals`: 216 passed, 2 skipped (live), 0 failed. This includes 15 new regression
  tests and 1 new contract baseline; the other 10 contract baselines are unchanged.
- Unit tests `Sprk.Bff.Api.Tests --filter FullyQualifiedName~Signals`: 5554 passed, 6 skipped, 0 failed (after the rebase on c438119bca; IntegrationTests unchanged at 216).
- ArchTests (full): 2845 passed, 0 failed.
- Publish size (`dotnet publish -c Release`, fresh `origin/master` c438119bca via `git archive` vs this branch rebased on it, same Python zip):
  194 vs 194 files; raw +34,180 bytes; zip 40.64 MB -> 40.66 MB (+18,699 bytes). No package change.
- Jest (`Spaarke.UI.Components`, DecisionRecords and Worklist): 15 suites, 322 tests passed.
- `tsc --noEmit` on the package: 9 errors, all pre-existing and none in changed files (`@spaarke/auth` not built, and `err` unknown
  in 3 unrelated files). eslint on the changed files: clean. `client-lint check --package ui-components`: ok.
- New baseline: `tests/integration/contract/Api/Signals/Baselines/worklist.200.decided.json`, recorded with `SPAARKE_GOLDEN_RECORD=1`.

## Fix round 1 (review of PR #3277)

- **F1: the Decided tab could return a permanent 503.** The record read was bounded at `MaxPages` before any core, communication or
  Signal filter ran. The gate writes a record for every assessed email (authorize and deny), and a Console User has Deep read on
  `sprk_decisionrecord` (D-144), so a busy business unit would pass the bound within 90 days. The read is now two queries, run at the
  same time, each with its own `MaxPages` bound and both keeping `WriterCreatedRecord` and the 90-day `sprk_decidedon` window:
  - `DecidedRecordsFetch`: records that resolved Signals (`sprk_steps not-like '%"resolvedSignalIds":[[]]%'`), with the same columns as
    before. This bounds about what the Signal-first read bounded.
  - `GateRecordsFetch`: the gate's records, found the way the writer finds one (`FindGateRecordAsync`): filed under a communication,
    `sprk_confirmedby` null, and no Signal resolved. The query has an **inner** link to `sprk_communication`, so only records whose email
    the caller can read come back and count. It selects only what the tab shows: no `sprk_steps`, `sprk_followons` or confirmer link.
  - **Deviation from the review's wording.** The review asked to identify gate records by `sprk_deciderrole`. No such column exists: the
    decider role is stored only inside `sprk_factsnapshot`. The columns used above are the writer's own identification of a gate
    record. The LIKE needs `[[]` because `[` opens a character class in a Dataverse LIKE.
  - The separate caller read of communications (`ReadReadableIdsAsync`) is gone, because the join replaces it. Its test
    (`DecidedSet_ACommunicationReadFault_Is503_NotAShorterList`) was removed: there is no such read left to fault.
- **F2:** the Shape comment now says the Decide lane can have a Not filed group of records (a gate record with no core record).
- **K2 scope:** only gate records are listed without a Signal; see Known limits.
- **D-227 (main session's request):** `rowTitle` now names the finding as `ruleShortName ?? title ?? subjectName ?? 'Decision'`, keeping
  the core-name prefix. A new jest case fails on the old order.
- **New tests:**
  - Gate records on email the caller cannot read, more than `MaxPages` pages of them, are neither returned nor counted (200). The join
    and the narrow column list are asserted, and the caller who can read them all gets a 503 (the must-fire twin).
  - Writer records that resolved no Signal and are not gate records, more than the bound, are neither listed nor counted. This includes
    a person's review filed under an email.
  - The records that resolved Signals are still bounded (503).
  - The gate writer's envelope text is pinned.
- **Fake changes:** `GateDecision` now files each record under a communication its readers can read, by default (`withoutCommunication`
  opts out). The fake evaluates LIKE / not-like on `sprk_steps`, null / not-null lookups and the inner communication join. A reviewed
  record carries `_sprk_confirmedby_value`.

## Known limits (K-class)

- A deactivated Signal's records are not listed: the template keeps `statecode eq 0`, so deactivation hides the item and its history,
  as before.
- A Signal-backed row shows the Signal's current status and resolved-on date. Both reviews of one Signal show the same item fields;
  only the record differs.
- A record whose `resolvedSignalIds` names a Signal it did not actually close is listed under that Signal, if the caller sees it. Only
  the writer writes the envelope, so the record is not forged.
- **Billing, Record Procedure and assign-firm records with no Signal are not listed** (fix round 1, review K2 scope). D-231 decided
  gate records only; the owner is to decide the rest. Such a record is read by neither Decided query, so it is not counted either.
- **K2: a gate record's core record is judged by the caller's own read of the record's core pair** (`ReadReadableCoresAsync`, the same
  rule as a Signal's core), not by any membership the gate computed when it wrote the record.
- **K4: a no-core record that is not a gate record is never listed, even to its owner.** A no-core gate record is listed only through the
  caller's read of its communication.
- A gate record on a core record the caller can read, filed under a communication the caller cannot read, is not listed. The inner join
  comes first, so that is stricter than the core rule alone. A caller with no Read on `sprk_communication` gets an empty gate read: the
  query answers 403, which is treated as "the records are omitted".
- The two Decided queries split the writer's records by a LIKE on the stored envelope's compact `"resolvedSignalIds":[]`. The gate
  writer's tests pin that text (`DecisionRecordWriterTests.GateDecision`, both outcomes). A record with no `sprk_steps` is in neither
  query; it was unclassifiable before as well.

## Found, not fixed

- None open. The `rowTitle` order (D-227) was fixed in fix round 1 at the main session's request.

rework: none task 226
