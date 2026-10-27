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
  empty flag is not listed: fail closed) and `modifiedon >= watermark`, plus every `sprk_servicerequest` changed since
  then (F4-b), the roots it carried, and the catch-up sweep's window (F4-a, below). At most
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
| `sprk_budgetbucket` | A budget's buckets (`sprk_budgetbucket.sprk_budget`, solution XML) | **Fixed (review F1-b):** lineage row + role-set entry. A Secure budget's buckets were not isolated before (2 rows on dev). The role entry is applied on an environment by `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` (dry run, `-Apply`, `-Verify`) — a post-merge live step that needs the owner's go, and must run BEFORE the BFF carrying it is deployed (until then the Secure pass's owner write of a bucket is refused, and the isolation census reports the missing privilege). Evidence is read-only: dev holds `prvReadsprk_BudgetBucket` (0e1b51ff-…), the Secure Record Owner role has no row for it. |
| `sprk_invoicelineitem` | Under an invoice (`sprk_invoice`) and a billing event (`sprk_billingevent`) | **Not changed — reported to the coordinator.** The entity IS defined in source (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_InvoiceLineItem/Entity.xml`, user/team-owned) but is not on dev (no `prvReadsprk_InvoiceLineItem` privilege). Once deployed it is the same gap the bucket was. |

**Service requests (review F4-b).** The ordinary pass now walks each row's service-request lookup
(`ExternalDataService.ServiceRequestRelations`, the one declaration: `sprk_regardingservicerequest` on nine tables, a
document's `sprk_relatedservicerequest`), and the job lists changed service requests. The SECURE walk is unchanged:
`SecureChildLineage` still has no service-request lookup, and the secure pass still refuses a service request as its root.

## ebilling-r1 hand-off

The ebilling-r1 invoice-family tables are not on master. When ebilling lands them, it adds each table to
`SecureChildLineage.Children` (with its lookups) and, where it is an intermediate, to `CoreAncestorResolver`
(`IntermediateRootColumns` / `ChildRecordEntities`), plus the codified role set and the census. They then follow a
reassigned ordinary parent by this same mechanism, and a Secure parent by the Secure pass, with no further code.

## Catch-up sweep (review F4-a) and its cost on dev

Each App Service instance walks every ordinary root once in the current mode (`MaxRootsPerRun` = 50 a run, cursor by
table then id, the Access Permission sweep's paging), then only the watermark decides; switching
`OrdinaryMovesWritesEnabled` starts it over. Dev, read-only counts 2026-10-27: 62 ordinary matters, 24 projects, 25 work
assignments, 6 service requests = 117 roots → 117 ÷ 50 = 3 runs × 2 minutes ≈ 6 minutes per instance per mode, at most
50 extra root walks a run during it, nothing after. A 10 000-root customer would take 200 runs ≈ 7 hours per instance.

## Files of documents the pass does not re-own (review F1-a) — NOT built, design question open

- Covered today (evidence test `TheJob_MovesTheFileOfAUserOwnedDocumentDirectlyUnderAMovedMatter`): a document filed
  DIRECTLY under the moved matter, project or work assignment, whatever its owner (the re-file pass's A3 listing), and any
  document the pass re-owns (its `modifiedon` changes).
- The gap: a document the pass does not re-own (user-owned, a non-default team, a Secure answer reported) filed BELOW a
  direct child (under an event of the moved matter).
- The prescribed fix (queue through `sprk_relocationpending`, settle in the existing step) was not built: that settle
  step relocates every ledger row with `RelocationPurpose.MakeSecure`, which moves whatever file the row shows, wider
  than the re-file's BFF-bound-item proof (B9); the ledger is the witness-bearing record of owed steps, written only
  under the relocation lock; a request entry needs a ledger format change and a purpose dispatch in the relocator. See
  the task report for the options.

## Known limits

- Grant rows (`sprk_externalrecordaccess`, owned by the root's team, D-11) do not follow an ordinary move.
- The F1-a gap above.
- Each changed ordinary root is walked in full (one query per lineage table per level, plus resolver reads per row), so
  a burst of edits to large matters is bounded only by `MaxRootsPerRun` (50).
