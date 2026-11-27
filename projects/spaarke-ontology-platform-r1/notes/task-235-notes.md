# Task 235 — Decision Records tab: Signal-less billing, Record Procedure and assign-firm records (#3283, DI-180, D-234)

Branch `fix/ontology-235-dr-tab-signal-less` from `origin/master` (6f4d7bc7e4). Rigor FULL (opus @ high, steps directional).

## Findings

- The Decided set (`GET /api/v1/signals/worklist?set=decided`, `SignalReadService.ReadDecidedRecordsAsync`) reads two bounded sets
  since task 226: the writer's records that resolved Signals (`DecidedRecordsFetch`) and the email-gate records inner-joined to their
  email (`GateRecordsFetch`). A writer record that resolved no Signal and is not a gate record is read by neither (task 226 K2 scope).
- The three kinds D-234 adds, and how the writer marks them:
  - **Record Procedure** (`DecisionRecordWriter.WriteRecordProcedureDecisionAsync`): `sprk_actioncode` is one of
    `DecisionActionCatalog.RecordProcedureActions`; `resolvedSignalIds` is always `[]`; filed under the core record itself (matter,
    project or work assignment, `InvoiceFamilyCoreRecordGuard.FilingCoreTables`).
  - **Billing** (`WriteBillingDecisionAsync`, through `BillingDecisionRecorder`): `sprk_actioncode` is one of
    `DecisionActionCatalog.BillingActions`; filed under the line or invoice, with the core pair derived from it.
  - **Assign firm** (`InvoiceAssignFirmService` → `BillingDecisionRecorder.RecordAsync` → the billing entry): the billing code
    `assign-firm` (`InvoiceAssignFirmService.AssignFirmActionCode`). It never resolves a Signal (decisions.md 095d-1 amendment: its
    `ResolvedSignalIds` is empty because the firm_missing Signal is shared).
  - The catalog keeps the three code sets disjoint from each other and from the Signals codes (the writer's P1 check), and
    `sprk_actioncode` is written only by the writer, from the catalog definition (`Content`). With `WriterCreatedRecord`
    (createdby = the writer, createdonbehalfby null; D-145a/task 223) it is a stored, writer-trusted property. No text is read.
- **Core and the caller trim.** Every record of these kinds carries its core pair and, written at create from the same core, the typed
  lineage lookup of that core (`DecisionRecordWriter.LineageColumnByCoreEntity`: `sprk_matter`, `sprk_project`, `sprk_workassignment`).
  A Console User has Deep read on `sprk_decisionrecord` (D-144), so a read bounded before any core filter would count records whose
  core the caller cannot read (the #3277 F1 503). An INNER link from the record's lineage lookup to its core table makes Dataverse
  return only the records whose core the caller reads, so only those count toward the bound.

## Design (D-234)

Each kind is its own read, per core table (3 kinds x 3 filing core tables = 9 reads), each:

- AS THE CALLER (`_user`, the same `ReadRecordPagesAsync` as the two task-226 reads), never app-only;
- `sprk_decidedon` last-x-days 90 and `WriterCreatedRecord` (the shared `DecidedRecordConditions`);
- `sprk_actioncode in (the kind's codes)`;
- `sprk_steps like %"resolvedSignalIds":_]%` (resolved no Signal), so a record that resolved a Signal stays only in the Signal-backed
  read and is listed once, under its Signal;
- an inner link to the core table on the record's lineage lookup;
- ordered decided-on desc, then id; paged up to `MaxPages`, then 503 (`too_many_<kind>_decision_records`), never a shorter list. A
  403 on page 1 (no Read on that core table) omits that read, a 401 or a refused later page is a 503: the gate read's rules.

The records join the gate records in `ListUnbackedRecordsAsync` (10b): kept only when the caller's own batched read of the core pair
returns it (`ReadReadableCoresAsync`), listed in their core's Decide group with their summary, `steps: null`, `followOns: []` and no
item (no Open). They are never listed Not filed: a record of these kinds with no readable core pair is not listed. Records with no
Signal are de-duplicated by id and ordered decided-on desc (stable, so the gate listing's order is unchanged). The new reads are
skipped for a one-Work-Item read (`onlySignalId`), which lists no record with no Signal.

Escalation trigger: not fired. Every kind is identified by stored, writer-trusted data (`sprk_actioncode` on a `WriterCreatedRecord`
row), and every read is bounded and trimmed by the caller's own read (no app-only read, no new column, no role change, no new route,
no new service, interface or DI registration: two static members and one fetch builder inside `SignalReadService`).

### Component justification (root CLAUDE.md §11)

No new service, interface, endpoint, DI registration, package or column. The new surface is inside `SignalReadService`:
`SignalLessKind`/`SignalLessKinds`, `SignalLessRecordColumns`, `SignalLessRecordsFetch`, `TryReadSignalLessRecord`.
1. Existing: `GateRecordsFetch` and `ReadRecordPagesAsync` (task 226), which the new reads reuse unchanged (paging, bound, 403/401
   rules, parser via `TryReadGateRecord`), and `ListUnbackedRecordsAsync` (10b), which lists them.
2. Extension: they extend the Decided read; the gate fetch cannot be widened, because its inner join on the communication is what
   trims gate records, and these kinds are trimmed by their core record.
3. Cost of doing nothing: D-234's records stay invisible on the tab (#3283).

## Changed existing code and tests (justified)

| Where | Was | Now | Why |
|---|---|---|---|
| `ReadDecidedRecordsAsync` | two reads | two reads plus 9 kind reads, first failure in order answers | D-234 |
| `ListUnbackedRecordsAsync` | gate records in read order | records with no Signal, de-duplicated by id, ordered decided-on desc (stable) | merge several reads; the gate read is already in that order, so its listing is unchanged |
| fake `World.Decision` | no action code, no lineage lookup | `actionCode`, and the typed lineage lookup of the core (as the writer) | model the writer |
| fake DR fetch | conditions on steps/lookups only | also `sprk_actioncode in`, and an inner core link trimmed by the caller's read of the core (403 when the caller has no Read on that table) | model Dataverse |

The task-226 test `DecidedSet_MustNotFire_AWriterRecordThatResolvedNoSignal_AndIsNotAGateRecord_IsNeitherListedNorCounted` is
unchanged and still passes: its records carry no kind's code, so no read returns them. The existing decided baselines
(`worklist.200.decided`, `worklist.200.decided-empty`) are unchanged.

## Tests

New `tests/integration/regression/Signals/Issue3283_DecisionRecordsTabSignalLessRecordsTests.cs` (part of `SignalReadRouteTests`,
linked into IntegrationTests, removed from the unit glob) and contract case `worklist.200.decided-signal-less`
(`SPAARKE_GOLDEN_RECORD=1`).

- Must fire: a Procedure (matter), an assign-firm (matter) and a billing (project) record are listed in their core's group, newest
  first, with summary, confirming user, `steps: null`, `followOns: []`, no item; a kind record and a gate record share a group.
- Must not fire: a secure family's records to a non-member; records on a core the caller cannot read (neither listed nor counted, and
  the caller who reads them all gets 503); a billing record that resolved a Signal (listed once, under it); a no-core record; a record
  with a Signals code; one decided before the window; a person's (not the writer's) row; a core table the caller has no Read on (only
  that read omitted).
- Bound: per kind (theory over the three), the read goes through the caller's client, pages, and past `MaxPages` is 503; two kinds each
  at `MaxPages` pages are both listed (separate bounds, not one shared). A static test pins the kinds to the writer's code sets (disjoint)
  and every fetch's conditions and inner core link.

### Fail-before evidence

`SignalReadService.cs` swapped for `origin/master`'s, and the one static test that names the new members cut from a copy of the test
file. `--filter DecidedSet|ContractBaseline_Worklist`: **10 failed, 37 passed**. Failing: every must-fire, bound and separation test
(3 theory cases), the core-join and 403 tests, the secure-family test (Bob's listing) and the new baseline. Passing on master: the two
must-not-fire controls master already had (Signal-backed billing listed once; no-core/no-kind/window/forged) and all task-226 tests.
The static pin test does not compile on master. Files restored afterwards; no stash.
