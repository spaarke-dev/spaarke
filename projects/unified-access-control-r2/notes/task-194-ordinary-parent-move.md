# Task 194 — children follow a reassigned ordinary parent (I-6 drift, #2339)

Owner round 128 item 3. Branch `fix/uac-194-ordinary-parent-move`.

## Escalation check (step 1) — does a platform Assign bump the core record's `modifiedon`?

**Yes.** Read-only evidence from spaarkedev1, 2026-10-27:

- Audit row `647076e0-9c3e-470c-a4ed-3d2db9ba5640`: action 13 (Assign) on `sprk_matter` CMRCL-285858
  (`f502bc5a-16a6-f111-aaad-7ced8ddc4cc6`), created `2026-10-19T09:59:54.379`, changing `ownerid` and
  `owningbusinessunit`.
- The matter's `modifiedon` reads `2026-10-19T09:59:54`, the same second as that Assign (the record's last change).

So the recent-changes pass can see an MDA reassignment by the core record's own `modifiedon`. The escalation trigger
did not fire.

## Design as built

- **No new job, table or column.** `SecureChildReconciler.ReconcileOrdinaryParentMoveAsync` is a new mode of the
  existing pass (same descendant walk, `ContextFor`, resolver and fixpoint rounds). `SecureChildReconciliationJob` lists
  the ordinary roots and calls it.
- **Trigger.** The job lists `sprk_project`, `sprk_matter` and `sprk_workassignment` with `sprk_issecure = false` (an
  empty flag is not listed: fail closed) and `modifiedon >= watermark`, plus the roots it carried. At most
  `MaxRootsPerRun` roots a run (carried first, then oldest change first).
- **Decide (per row, against its current owner).**
  - Owned by the Secure Record owner team, by any team in the Secure Record business unit, or whose own business unit is
    that one: untouched and not reported (the Secure pass owns it).
  - Resolver refuses: `Refused`, reported, not moved, the root is carried.
  - Resolver gives the Secure team (or a team in the Secure Record business unit): untouched, reported ("only the secure
    pass moves a row into isolation").
  - Already on the resolver's team: `AlreadyCorrect`.
  - User-owned, or owned by a team that is not a business unit's default team: untouched; reported when the answer is in
    another business unit.
  - Otherwise (a default team of one ordinary business unit, the answer another): an ordinary move.
  - Multi-parent: the resolver's own answer (primary parent `facts[0]` = the row's first ownership-parent lookup).
- **Write.** Through `ReownOrdinaryRowCoreAsync`, the member `ReownOrdinaryRowAsync` (task 183) now forwards to: the
  same guards (Secure Record business unit of the row, of its owner and of the target, refused fresh; user-owned
  refused), the previous owner logged first, an If-Match owner write on the version read, eviction, read-back. The
  public `ReownOrdinaryRowAsync` keeps its two-table contract; only the reconciler may pass any lineage child.
- **Never isolation.** Nothing is moved into or out of the Secure Record owner team. The pass never decides a row owned
  in the Secure Record business unit, and the write re-checks both sides. A secure, isolated or unreadable-flag root is
  `NotApplicable`.
- **Not in this mode:** the root's grant rows and the platform-cascade rows (SharePoint locations, moved by the root's own
  Assign).
- **Switches.** `SecureChild:Reconciliation:OrdinaryMovesWritesEnabled` (default false, report-only),
  `SecureChild:Reconciliation:MaxOrdinaryMovesPerRun` (default 200 owner writes a run), the shared `MaxRootsPerRun`.
  The recent-changes emergency stop also stops it. Report: `ResultJson.ordinaryMoves` (counts, `changes` capped at
  `MaxSampledChanges`).
- **Window and carry.** Its own watermark, moved only after a complete listing in a run that WROTE (the Secure pass's
  rule), so a report-only run looks back one hour each time. A root left incomplete, stopped by the write cap, or past
  the root cap is carried (writes on). Only a writing pass that leaves a root incomplete (not merely capped) or cannot
  list fails the run.
- **Files.** Step 3 suggested a new `RelocationPurpose.BusinessUnitMove` queued through `sprk_relocationpending`. Not
  built (CLAUDE.md §11: the existing component covers it):
  - A FILED document's container derives from the record it is filed under (that record's business unit,
    `RecordContainerResolver.DeriveDocumentContainersAsync` → `ResolveForRecordAsync`); only an UNFILED document uses its
    own row's business unit.
  - The job's existing re-file pass (#2159, purpose `Refile`) already lists every document whose own `modifiedon` is in
    its window, and every document filed directly under a changed matter, project or work assignment (A3). It moves a
    file whose container no longer matches, with the widening guard that holds every Secure-owned document (no purpose
    moves a file out of isolation).
  - The ordinary pass runs BEFORE the re-file pass in the same run, and every re-own changes the document's
    `modifiedon`, so each re-owned document's file is examined and moved in the same run, capped by
    `MaxRelocationsPerRun`.
  - `sprk_relocationpending` is a ledger of steps an earlier move still owes, not a request queue; a "pending" entry with
    no move behind it would need a format change for no new behaviour.

## Lineage completeness (step 5)

Compared `SecureChildLineage.Children` (28 tables) with the codified role set (`config/secure-record-owner-role.json`,
kind `child`: 28 tables, identical), `CoreAncestorResolver.ChildRecordEntities` / `IntermediateRootColumns` (all
included), and the I-6 census (`RecordOwnerAssignmentCensusTests.ChildTables`, 34 names).

In the census list but not walked:

| Table | Why | Disposition |
|---|---|---|
| `sprk_workassignment`, `sprk_servicerequest` | Roots of their own, not children | n/a (a work assignment is its own ordinary root here) |
| `sprk_communicationchannelref`, `sprk_processingjob` | Waived in the census (transport key; job tracking, not a child) | n/a |
| `sprk_invoicelineitem`, `sprk_budgetbucket` | Content rows under an invoice / a budget; not in the lineage map or the role set | **Known limit; needs an issue** — adding them changes the Secure synchronizer's map and `SecureFamilyConsistencyTests` |

Also not reached: children filed ONLY under a service request (the lineage deliberately has no service-request lookups;
`SecureChildLineage` remarks). **Known limit; needs an issue.** The task goal names service requests; the acceptance
criteria name matter, project and work assignment.

## ebilling-r1 hand-off

The ebilling-r1 invoice-family tables are not on master. When ebilling lands them, it adds each table to
`SecureChildLineage.Children` (with its lookups) and, where it is an intermediate, to `CoreAncestorResolver`
(`IntermediateRootColumns` / `ChildRecordEntities`), plus the codified role set and the census. They then follow a
reassigned ordinary parent by this same mechanism, and a Secure parent by the Secure pass, with no further code.

## Known limits

- A reassignment made more than one hour before the switch is first turned on (or before an App Service restart that
  lost a carried root) is reached only when the record changes again (any edit puts it back in the window).
- Grant rows (`sprk_externalrecordaccess`, owned by the root's team, D-11) do not follow an ordinary move.
- A user-owned document filed under an event (not re-owned) whose file derives from the event's new business unit is
  not listed by the re-file pass until its own row changes.
- Each changed ordinary root is walked in full (one query per lineage table per level, plus resolver reads per row), so
  a burst of edits to large matters is bounded only by `MaxRootsPerRun` (50).
