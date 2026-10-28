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
| `sprk_invoicelineitem` | Under an invoice (`sprk_invoice`) and a billing event (`sprk_billingevent`) | **Left out of the lineage (coordinator decision):** adding a table that is not deployed would make the Secure pass query a missing entity set on dev; the coordinator files an issue to add it to the lineage and the role set when it is deployed. The entity IS defined in source (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_InvoiceLineItem/Entity.xml`, user/team-owned) but is not on dev (no `prvReadsprk_InvoiceLineItem` privilege). Once deployed it is the same gap the bucket was. |

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

## Files of documents the pass does not re-own (review F1-a) — built as option A

- Covered today (evidence test `TheJob_MovesTheFileOfAUserOwnedDocumentDirectlyUnderAMovedMatter`): a document filed
  DIRECTLY under the moved matter, project or work assignment, whatever its owner (the re-file pass's A3 listing), and any
  document the pass re-owns (its `modifiedon` changes).
- The gap: a document the pass does not re-own (user-owned, a non-default team, a Secure answer reported) filed BELOW a
  direct child (under an event of the moved matter).
- Not through `sprk_relocationpending`: its settle step relocates every ledger row with `RelocationPurpose.MakeSecure`,
  which moves whatever file the row shows (wider than the re-file's BFF-bound-item proof, B9), and the ledger is the
  witness-bearing record of owed steps, written only under the relocation lock.
- **Built (coordinator decision, option A).** With writes on, every document of a root's walk except one held in
  isolation / the Secure Record business unit, one the rule answers Secure (its container answer is a Secure record's),
  and one whose owner could not be decided or was refused (`SecureChildReconcileReport.OrdinaryHandOffDocumentIds`) is
  handed to the re-file pass as a carried row (`_ordinaryHandOffs`, read with its carry). It is examined and moved under
  the Refile purpose's guards, `MaxRelocationsPerRun` and the examination budget; the relocator's own refusals stand.
- **Back-pressure.** At most `MaxCarriedRows` minus (re-file carry ∪ hand-offs not yet reached). A root whose documents do
  not all fit is deferred (`ResultJson.ordinaryMoves.deferredRoots`): a recent root is carried, a swept root holds the
  sweep cursor just before it. Hand-offs the re-file pass has not reached stay hand-offs and are never truncated; one it
  reached leaves (a failure joins its normal carry). Report-only hands nothing off (`documentsToHandOff` counts them).

## Known limits

- Grant rows (`sprk_externalrecordaccess`, owned by the root's team, D-11) do not follow an ordinary move.
- Hand-offs and the re-file carry are per instance, like every window of the job: a restart loses them, and the
  catch-up sweep (F4-a) walks every root again, handing the documents off anew.
- Each changed ordinary root is walked in full (one query per lineage table per level, plus resolver reads per row), so
  a burst of edits to large matters is bounded only by `MaxRootsPerRun` (50).

## 194b: one deterministic ownership question per existing row

**Defect (found in the post-deploy report-only gate).** The ordinary-moves pass, the secure pass and `ReparentAsync` took
an existing row's primary parent from the order Dataverse returned its lookup attributes. That order is not the writer's
order, so the reconciler could plan a move to a different team than the row's writer chose (49 of 52 planned moves on dev).

**A. A fixed primary per table, shared by the writers and the reconciler.**
- `RecordOwnershipContext.InPrimaryOrder` ranks a row's ownership lookups by its table's `SecureChildLineage` declaration
  order (the order is now meaningful and stated in the class remarks); `OrderedParentsOf(row)` uses it.
- `sprk_signal` / `sprk_decisionrecord`: `ForExistingRowAsync` asks the writer's question: core
  (`sprk_corerecordtype` + `sprk_corerecordid`, the catalog read through `CoreAncestorResolver.CoreTableOfRecordTypeAsync`),
  then the subject (`SignalWriter.SubjectEntityOf`), then the subject's core-ancestor stamps re-derived through
  `CoreAncestorResolver.ResolveStampsAsync` (read-only), then the row's other lookups. Parity tests compare the FULL ordered
  parent list against `SignalWriter.OwnershipContextOf` and `DecisionRecordWriter.OwnershipContextOf`.
- A Signal or Decision Record with NO core record is reported, never moved: its owner is its To Do assignee's team (D-134),
  kept by `TodoAssigneeOwnerReconciliationJob`. An unknown core type or an unreadable catalog row is Undetermined (Failed,
  never a guess).
- `sprk_analysis`: its document first. `sprk_analysisoutput`: its analysis. `sprk_document`: parent document, then core
  lookups (matter, project, work assignment), then `sprk_related*`.
- Used by `SecureChildReconciler` (`ContextFor`, both passes) and both `ReparentAsync` paths. Secure-if-any is still read
  over every parent.

**B. Look through a user-owned FILED primary child (I-6).** In `ResolveFromParentsAsync`, a primary that is a child-table
row (`IsReparentableChild`) with no owning team and with a filing answers with that filing (the same look-through the
content-row rule already used, generalised, not duplicated). Bounded by `MaxLineageDepth`; past it the resolver REFUSES
(`ParentUnresolved`), never "ordinary". A core record (matter, project, work assignment, service request) is a root and
is never looked through. An unfiled user-owned primary keeps its own unit. `SignalWriter`'s check is unchanged.

Create-time writers whose answer B changes (only when the primary is a user-owned, filed child): analysis create/promote
(`AnalysisEndpoints`, `AppOnlyAnalysisService`) for a user-owned filed document; the
attachment child (`EmailAttachmentProcessor`) of a user-owned filed parent document; a no-core Signal or Decision Record
whose subject is a user-owned filed child and that has no assignee; content-row chains deeper than one level. Isolation is
never relaxed: the look-through keeps the original parents in the secure-if-any set (same set or a superset).

**C. Time slice.** `SecureChildReconciliationJob.OrdinaryTimeSlice` (60 s), checked between roots like
`RefileTimeSlice`. Roots not reached are carried (recent) or held by the sweep cursor (swept); none is dropped.
`ResultJson.ordinaryMoves.timeSliceReached` and the summary log report it.

**Dev measurement (read-only, 2026-10-27).**
- `sprk_signal`: 191 with a core (164 matter, 6 project, 21 work assignment): every one already in its core's business
  unit, so 0 differ from the 194b answer (= SignalWriter's). 13 with no core: now reported, not decided.
- `sprk_decisionrecord`: 24 with a core (18 matter, 1 project, 1 service request, 4 work assignment): 0 differ. 3 with no
  core: reported.
- `sprk_analysis`: none has a user-owned document (B changes nothing on dev). 69 user-owned analyses sit in a different
  unit from their document; user-owned rows are reported, not moved, before and after.
- `sprk_document`: no child of a user-owned parent document; the 7 children in another unit from their (team-owned)
  parent are user-owned (reported). 14 team-owned documents name both a matter (BU1) and a communication in another unit:
  before 194b their answer depended on attribute order (the communication's unit when read first); after, the matter
  decides, which is where they already are.
- `sprk_event`: 2 name both a matter and a project; the matter now decides.

**Fix round, slice 1 (adversarial review at 3fc7ef2b4f: F1-a, F4, K2, To Do parity of F3).**
- F1-a: a regarding-pair host table ranks its SUBJECT (the lookup that repeats `sprk_regardingrecordid`,
  `RecordOwnershipContext.SubjectColumnOf`, through `SignalWriter.SubjectEntityOf`) by its writer's rule. `sprk_todo`: the
  subject, then `sprk_regardingdocument`, then `sprk_regardingcommunication`, then the lineage order (OfficeService: "the
  record regarding first, then the document or email"). `sprk_signal` / `sprk_decisionrecord`: the typed core lookups, then
  the subject, then the lineage order. Before, a To Do regarding a matter and created from an unfiled email took the email's
  unit. `ReparentAsync` reads the pair from the row as it stands; a change that moves the subject lookup without the pair
  (the pair is not part of `RecordReparent`) falls back to the carrier and lineage order for that one decision, and the
  reconciler re-asks with the row's pair afterwards.
- F4: `RecordOwnershipContext.ForChild` lists its parents primary first, then in the table's primary order, and with no
  explicit primary takes the first of that order; `OwnedChildWrite.ParentsOf` (create_record, `BudgetInquiryExecutor`,
  `RecordCreationService`) orders the payload's lookups the same way, reading the pair from the body.
- K2: `RecordThreadFiling.ParentsOf` uses `OrderedParentsOf`.
- Parity (F3, To Do): `TodoOwnershipParityTests` drives the Office route, `TodoGenerationService` and
  `InquiryReplyTodoCreator` and compares the writer's full ordered parent list with the reconciler's question.
- Dev (read-only, 2026-10-27): 69 To Dos; 2 carry a document beside a matter, both in the matter's unit; none names two
  record lookups. 0 To Do answers change under F1-a.

**Fix round, slice 2 (F1-b, F1-c, F1-d, K3, K5, F2, Signal and Decision Record parity of F3).**
- F1-c: the assignee rule (`ExistingRowOwnership.NoCoreOwnerRule`) is set only for a no-core row whose subject is a To Do
  AND that `TodoAssigneeOwnerReconciliationJob` selects: `SelectsAsync`, the job's own steps extracted as shared statics
  (`ClassifyToDoAsync`, `KeepsRowsOf`, `HasLiveCoreStamp`, `IsOwnedByUser`), which the job's `DecideRowAsync` also calls. Every
  other no-core row is decided over `[subject, ..rest]`, as its writer decided it. A To Do read that faults, or no links, makes
  the row undetermined.
- F1-d: a Decision Record's question appends every resolved Signal's ownership lookups (the job's
  `ResolvedSignalSubjectsAsync`, extracted; a fault makes it undetermined). One never-out predicate,
  `RecordOwnershipContext.DecisionRecordHeldInIsolation`, has three callers: a root's secure pass on a move OUT of a row
  isolated at the pass's start (Unsecure included: held, reported Untouched with the reason; NOT the child pass after an
  F3-permitted re-file, `AfterRefileAsync`, whose release ontology task 144 criterion 3 pins — open question for the
  coordinator), the ordinary pass at its isolated /
  Secure-BU check (reported), and `ReparentAsync` after the owner is resolved and before the F3 check or any write
  (refused `ParentUndetermined`, logged). A move into isolation is never held.
- K3: the secure pass never moves a job-kept no-core row OUT of a start-isolated position (held, reported with the job's
  rule); a move IN still happens.
- F1-b: an undetermined row in the secure pass is resolved over the parents it stores; a Secure answer moves it IN (or keeps
  it isolated: secure-if-any over a subset that is secure is secure over the whole). Any other answer still fails.
- K5: `ExistingRowReads` caches the catalog row by record type and the stamps by subject for one pass.
- F3: parity goes through the real writers: `SignalWriter.OwnershipOfAsync` (core through `PickStampAsync`, and the owner)
  and `DecisionRecordWriter.OwnershipParentsForAsync` (the write's own filing and owner step: the regarding builder, the derived cores, `SignalSubjects`).
- F2 count (read-only, dev, 2026-10-27): 13 no-core Signals (2 event subjects, 11 To Do subjects; 2 of the To Dos filed under
  an unfiled user-owned event) and 3 no-core Decision Records (unfiled communications). 0 writer answers change under B. B
  stays: it lives in the resolver (`ResolveFromParentsAsync`), so the writers' no-core contexts and the reconciler's no-core
  question make the same choice from one place (pinned by the owner-equality parity test).
- Reparent limit: no isolation decision depends on the fallback order. Secure-if-any reads every parent and every
  un-team-owned parent's filing (`ReadUnownedChildLineageAsync`) regardless of order; the order only picks the ordinary team,
  and a primary look-through past its bound refuses.

**Fix round, slice 3 (analysis, attachment and .eml parity of F3).** The three remaining parity tests call the writers'
own builders, in `WriterBuilderOwnershipParityTests`: `AppOnlyAnalysisService.OwnershipOfNewAnalysis` (now also used by the
`AnalysisEndpoints` create route and `AnalysisResultPersistence`), `AnalysisEndpoints.OwnershipOfPromotedAnalysis`,
`EmailAttachmentProcessor.OwnershipOfAttachmentDocument`, `IncomingCommunicationProcessor.ArchivedEmlDocument` (the archive row
with its lookups and owner) and `IncomingAssociationResolver.OwnershipContextOf` (the static tail of
`OwnershipContextForNewRecordAsync`: the communication's question, which the archive's owner comes from). No answer changes.

**Known gaps (not 194b).**
- A Decision Record's existing-row question names every ownership lookup of each resolved Signal (the job's read), a superset
  of the writer's `SignalSubjects` when a Signal carries more than its subject (secure-if-any only reads more).
- A gate Decision Record on an unfiled communication is owned by the communication's user (D-144); the reconciler asks it as
  a plain row (keep-creator), so it is never moved.
