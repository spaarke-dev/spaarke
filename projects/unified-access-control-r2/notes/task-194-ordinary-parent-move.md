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

**Fix round R, run R1 (A1, A2; ontology answer (c)).**
- A1: a Decision Record's existing-row question takes its Signal subjects from `sprk_steps.resolvedSignalIds` (written once
  at create, never truncated), not from the Signals' `sprk_decisionrecord` link: `DecisionCommitService.ResolvedSignalSubjectsOfRecordAsync`
  reads the listed Signals by id, in any state, in pages of `ResolvedSignalReadPage` (100; no 5,000 cap, verifier K1), and
  applies `KnownSignalSubjectsOf`, the rule `SignalSubjectsOf` now delegates to (each Signal's subject only, stored order,
  de-duplicated). The question is `ExistingRowOwnership.Incomplete` (decided over what is known) when `sprk_steps` is missing
  or unparseable or has no `resolvedSignalIds`, a listed Signal is unreadable or gone, or a subject table cannot be derived.
- `DecisionRecordHeldInIsolation` takes the question's incompleteness and holds only then. It is one predicate with the same
  callers, in every pass: `refileChildPass` is removed. `ReparentAsync` passes `ReparentQuestionIncomplete`, because a
  re-file asks about its own lookups, so its behaviour is unchanged until R2 (A5). The FK reader is the To Do job's own again.
- A2: a row held in isolation by a pass that may release (F1-d or K3) loses its mirror through
  `SecureChildShareSynchronizer.RemoveHeldMirrorAsync` and stays on the Secure team. A removal that does not complete marks
  the row failed for the next pass. The report lists `HeldInIsolation`; `SecureChildPassSummary` carries `held` and
  `heldRecords` (at most 50, with why) on the Unsecure and provisioning responses. Holding does not make a pass incomplete.
- Ontology criterion 3's seed now writes the Decision Record's `sprk_steps` as `DecisionRecordWriter` does; its assertions and
  wording are unchanged.
- Dev count (read-only, 2026-10-27): 28 Decision Records, all 28 list `resolvedSignalIds`: 0 lack the key, and 0 of those are
  Secure-owned (2 records are Secure-owned overall).

**Known gaps (not 194b).**
- A gate Decision Record on an unfiled communication is owned by the communication's user (D-144); the reconciler asks it as
  a plain row (keep-creator), so it is never moved.

## Fix round R, runs R2–R5 (history and decisions)

**R2 (09db6130b3 … 72a15de9ed).**
- A3: a user-owned primary child that loops (an event and its communication filed under each other, two documents) no
  longer runs to `MaxLineageDepth` and refuses. The look-through carries the primaries it has seen; on a revisit or at the
  bound it decides secure-if-any over the parents it holds. Only an answer that would be ordinary refuses.
- A4: a second never-out check in the re-file settle. Dropped in R3 (coordinator: A5 makes it unreachable).
- A5: `ReparentAsync` refuses a generic re-file of a Signal or a Decision Record before anything is read or written
  (`WriterOwnedFilingRefusal`). Only their writers file them. Dev: 0 field-mapping profiles, playbook nodes or configs
  name either table.
- A6: `TodoAssigneeOwnerReconciliationJob` reads a Decision Record's resolved Signals from `sprk_steps.resolvedSignalIds`
  (the resolver's one source), not from the Signals' link. It never writes an ordinary owner on part of the question.

**R3 (1c69bd74cb … 454e0f848d).**
- A4 and `ReparentAsync`'s unreachable Decision Record never-out branch removed.
- B1: `IncomingAssociationResolver.OwnershipContextOf` orders the writes and the stamps together, keyed by the lookup each
  is written to, in `sprk_communication`'s order. **Deviation, accepted:** the brief asked for the writes ordered "before
  the stamps"; ordering both together is what the reconciler reads. Dev: 6 communications hold a matter and a project, 0
  in different business units.
- B2: the Compose copy asks its inherited links in `sprk_document`'s order.

**R3b (e25cb27461 … 87a185061f): one builder.**
- `RecordOwnershipContext.ForRow(table, column → parent, pairId)` is the one builder of a child row's question. It orders
  through `InPrimaryOrder`, the order the reconciler reads the written row in. `ForChild` routes through it.
- **Decision 2 (coordinator): the table's order wins.** An explicit caller-named primary ("regarding first") gives way to
  the table's order: `TaskActionCore`, Office, `TodoGenerationService`, `InquiryReplyTodoCreator`. The CommunicationService
  secure-links copy loses its "first secure link" override too.
- Two behaviour changes follow decision 2:
  - an event asks its stamped matter before its regarding project (the owner was asked over the regarding lookup alone);
  - a budget revision asks its budget before its matter. The theory
    `ReviseBudget_TheRevisionIsOwnedByTheTeamTheOwnershipRuleNames` was updated from matter-first to budget-first.
- Decision 1: contexts that are not about a child row's owner are out of scope: `ContentOf`, `GateDecisionRecordSharing`,
  the secure-root cascade, the To Do job's own parent list. They are the guard's permanent exemptions.
- The closed opt-out: `ForParentsInWriterOrder`, used only by `SignalWriter` and `DecisionRecordWriter`, whose order is their
  table's rule. `WriterOrderOptOutAllowListTests` fails on any other caller and on a stale entry.
- `SecureChildLineage`: the comment now says a service request IS an ordinary ownership and filing parent
  (`FilingParentOf`); only the Secure lineage map leaves it out.

**R4 (bb09c42cda): real-column keying.** Every extra parent is keyed under the real column that files it, so `ForRow`
ranks it as the reconciler does:
- a message's thread filing is keyed under `sprk_communicationthread`, which `sprk_communication`'s order ranks after the
  message's own lookups (`RecordThreadFiling.FilingLookupsOf`);
- a project's ADR-024 pair parent is keyed under `sprk_regardingrecordid`.
- **`RootLookupOrder` deviation, approved by the coordinator.** The pair needs a rank on `sprk_project`, which is a root.
  Adding `sprk_project` to `SecureChildLineage.Children` would have made projects reconciled children. A separate
  `SecureChildLineage.RootLookupOrder` ranks it instead. After the master merge (R5), `RecordFamilyEdgesGuardTests` (which
  reads `Children`) passes unchanged.
- **Repair attribute-order defect, found and fixed.** `RecordCreationService` Repair asked a row's parents in the order
  Dataverse returned its attributes: the #2339 class. It now asks through `ForRow` (`OwnerQuestionOf`), the same question
  Create and the reconciler ask. Dev: 0 rows affected (Repair has no live caller; K-c).

**R4-2 (defdadbb8e … e75511f6ec).**
- Spend signals: `SignalEvaluationService` asks once per firing snapshot over the matter and the snapshot
  (`ForRow("sprk_spendsignal", …)`, matter before snapshot). A refused snapshot writes no signal for that snapshot.
- **Office communication defect, fixed.** An email-derived document is written with its association AND
  `sprk_relatedcommunication`, but the save and the upload worker asked about the association alone. A secure
  communication beside an ordinary association was seen only by the reconciler's next pass. The save now resolves the
  communication first (the capture's id, or a read by Message-ID) and asks over both
  (`OfficeDocumentPersistence.DocumentOwnershipOf`). A failed read refuses the save. The worker asks the same question
  over the payload's communication. (V-fix 4 changed the failed read's code from OFFICE_022 to OFFICE_014; see below.)
- **Contact ruling (coordinator): the reconciler is right.** A contact is never an ownership parent; people lookups are
  never family. A document filed to a contact is filed only by its communication, and is unfiled when that is empty.
  `AssociationTargetOwnershipOf` was removed. Dev: 0 documents hold `sprk_relatedcontact`.

**R4-3 (d92dec5a0e): B4.**
- Carried roots run least-recently-attempted first.
- The time slice never stops a group (listed, swept) before its first root, so every run advances at least one root of
  each group.
- A report-only run resumes after the last root the previous report-only run reached (`ordinaryMoves.reportCursor`),
  wrapping around. The cursor is cleared when a run reaches every root, and by a writing run.

**R5.**
- Merged `origin/master` (9280a282d3): field-mapping 070 `RecordFamilyEdges`, and 037's copied Assigned To default. The
  default is still applied in `OwnedChildWrite.CreateAsync` before the caller check, the secure-root plan, the
  `OwnershipOf` / `ForRow` question and the create.
- One Secure Record business-unit lookup (6666ab5d5e, round-130 rule 3):
  `SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync`, beside `IsInSecureBusinessUnit`. The private copy in
  `RecordOwnershipResolver` and the copy in `SecureChildShareSynchronizer` are gone. No behaviour change: the resolver
  passes its logger, so its ambiguity error log stays.

**V-fix (after both verifiers on bd5208d0b5).**
- **1, isolation: a task regarding an ownership parent that `sprk_event` cannot file is refused** (79edcf637e).
  `TaskActionCore` dropped such a regarding (no column for it) and asked about no parent, so the task landed in the acting
  user's team even under a Secure document, To Do, agreement or billing event. Now `TaskActionCore.RegardingRefusalOf`
  refuses it, fail closed, with `task_regarding_not_filable`: the playbook node fails (`ValidationFailed`), the ad-hoc
  route answers 422 `CREATE_TASK_FAILED` naming the code, and nothing is written. A regarding type that is not an
  ownership parent and has no column (for example `systemuser`) still creates the task unfiled. The other `ForChild`
  callers were checked for the same lost-target pattern; none loses its primary:
  - Office To Do: every `TodoRegardingMap` column it asks about is written.
  - `InquiryReplyTodoCreator`: the matter is written through `ApplyResolverFieldsAsync`.
  - `TodoGenerationService`: the builder throws on an unmapped type; the content source is the permanent exemption above.
  - `CommunicationService` secure copy: its links are written as columns.
- **2, isolation: the ordinary pass never writes on an incomplete question** (fed2e4ac08). A Decision Record whose
  resolved Signals cannot all be read now gets `Failed` (with the reason) in `DecideOrdinaryRowCoreAsync` when the answer
  is ordinary, instead of an owner computed from part of its parents. A question that is secure on what it could read is
  still reported for the secure pass.
- **3, parity: `sprk_canonicaldocument` in the writers' question: STOPPED, not changed.** Task 146's comment in
  `ComposeCreateOnSavePromoter` (~:252) gives the reason Compose excludes the link: "a byte-identical canonical is not a
  parent; following it could hand the saver's own document to another record's team". Task 148 R5 recorded the
  remaining difference as an under-share only. Including the link as an ownership parent would make an ordinary copy
  take the canonical's business unit (the saver's document handed to another business unit's team), and the link is
  severed when the copy diverges, so the question would change after the fact. Reported to the coordinator for a
  decision (for example: the canonical counts for secure-if-any only, never as the ordinary primary).
- **4, behaviour: a transient communication read on an Office save is retryable** (5b5ffee272). A failed read of the
  email's communication now answers 502 `OFFICE_014` (`DataverseError`, `retryable: true`), which the add-in's
  `errorMessages.ts` already treats as recoverable. `OFFICE_022` stays for a real owner refusal (403, not recoverable).
- **5, performance: per-pass lookup cache** (cb18593a6e). Each reconcile pass shares a `RecordOwnerLookupCache`
  (`RecordOwnershipContext.LookupCache`): the Secure Record business unit (primed from the ordinary pass's own read) and
  each business unit's default owner team are read once per pass, not per row. The cache lives exactly as long as the
  pass, and only answers are kept. Test shape (12 events under a moved matter): Secure Record business-unit reads 14 → 2
  (the pass's fixed setup reads), default-team reads 12 → 1, so 2 reads saved per row; on verifier B's dev shape (about
  180 reads per root) that removes 2 of each row's reads. Not cached, on purpose: `ReownOrdinaryRowCoreAsync`'s
  pre-write guard still reads the Secure Record business unit fresh before each write.
- **6, guard: builders matched by metadata identity** (8333a22a19). See the guard baseline below.
- **7, test: the event writer is compared with its written row** (cc8365fd81). `EventOwnershipContextTests` runs the real
  handler and compares its question with `OrderedParentsOf` over the Web API body it wrote. No hand-written list.

### Known limits accepted in R (recorded, not filed separately unless listed under "Issues to file")

- **Capture before refusal (R4-2, widened by V-fix).** The Office save runs the email capture before the owner question,
  so every email or attachment save that is then refused, for any reason (an unreadable communication, now a retryable
  OFFICE_014, or an owner refusal, OFFICE_022), has already captured the email. The capture is best-effort and idempotent
  on the Message-ID, as mailbox intake would make it, so a retry does not duplicate it.
- **Email saved to an Event or To Do association (V-fix).** `sprk_document`'s primary order
  (`SecureChildLineage.DocumentPrimaryRank`) ranks the parent document, then the matter, project or work assignment, then
  the other links in declaration order. An email saved to an Event or To Do association therefore takes its business
  unit from the communication (`sprk_relatedcommunication`), not from the event or To Do, when the two are in different
  ordinary business units. Writer and reconciler agree (one order). Secure-if-any reads both. Dev: 0 rows.
- **CommunicationService case 2 (R3b-2).** The outbound secure-links copy, when all its links are ordinary and in
  different business units, is now owned by `sprk_document`'s primary (for example the project) rather than the first
  link named. With any secure link it is the Secure team in either order (proven through the real resolver). Dev: 0
  rows (25 email-attachment documents carry `sprk_relatedcommunication`, none carries a record link).
- **In-memory report-only cursor (coordinator ruling, R4).** `ordinaryMoves.reportCursor` lives in the job singleton,
  like every window the job keeps. The job has no durable state store, and none is added. A restart starts the report-only
  window from its first root.
- **5 dev documents (data only).** Email-derived documents whose communication sits under a different ordinary matter
  than the document's own association: `dc70ebdf`, `fe41f0c5`, `0342f0c5`, `0742f0c5`, `73910392`. None of the matters
  is secure. Under the ordinary rule the document's own lookups decide, in the table's order.

### The guard baseline (`ChildRowOwnershipBuilderGuardTests`)

Since V-fix 6, `RecordOwnershipContext`'s own builders are exempt by metadata identity (each pinned by its signature), not
by name, so a new overload that shares a builder's name is scanned like any other caller (control:
`Detector_ExemptsABuilderByIdentity_NotByName`). **Remaining guard K:** the exemptions and the ratchet below are still
matched by (type, method) NAME, and the guard sees only how a question is BUILT: it cannot check that the columns a
writer hands `ForRow` are the columns it writes (`ForRow` semantics cannot be scanned from IL). The per-writer parity
tests (`OrderedParentsOf(written)` against the writer's question) carry that.

Permanent exemptions (not a child row's owner in a table's order):
- `RecordOwnershipContext.ContentOf`
- `GateDecisionRecordSharing.AskOwnerAsync`
- `SecureRootInheritance.UnsecureFollowingParentsAsync`
- `TodoAssigneeOwnerReconciliationJob.ParentsAnswerAsync`
- `SecureChildReconciler.Pass.ContextFor` (the reference question)
- `SecureChildReconciler.PlaceCascadeRowsAsync`
- `TodoGenerationService.CreateTodoAsync` (no column holds a standalone to-do's content source)
- `GrantExternalAccessEndpoint.ResolveGrantOwnerAsync`
- `ExternalIntakeEndpoints.SubmitServiceRequestAsync`
- `RecordOwnershipResolver.ResolveReparentOwnerAsync`
- `OntologyAdminAuthoringService.CheckVersionOwnerAsync` (a secure-if-any gate that binds no owner)

Ratchet (single-parent sites, so order decides nothing; move to `ForRow` when touched):

| Site | Reason |
|---|---|
| `ExternalProjectDataEndpoints.ResolveChildOwnerAsync` | Single parent: the route's root |
| `AnalysisResultPersistence.PersistReviewMemoAsync` | Single parent: the analysis |
| `AnalysisResultPersistence.StoreDocumentProfileOutputsAsync` | Single parent: the analysis |
| `AppOnlyAnalysisService.ExecutePlaybookAnalysisAsync` | Single parent: the analysis |
| `InvoiceReviewService.CreateInvoiceRecordAsync` | Single parent: the matter (the vendor organisation is not an ownership parent) |
| `SpendSnapshotService.ResolveSnapshotOwnerAsync` | Single parent: the matter OR the project, one per path |

### Dev finding: budget revision `e4e5dbe6-8fbf-f111-aaaf-0022482913fc` (read-only, 2026-10-28)

**Facts:**
- The revision is owned by a USER (the developer account), in the root business unit.
- Its budget `0f7f39b7` and its matter `b68299c6` (REAL-2026-123456.02, not secure) are owned by the default team of
  Spaarke Business Unit 1.
- The audit shows how it got there:
  - the matter has been owned in BU 1 since 2026-09-09;
  - the revision was created on 2026-10-03 by the ontology R1 task-005 seed script, a direct Dataverse create under the
    developer's own identity, outside the BFF, so Dataverse made the creator its owner;
  - the budget, also user-owned, was assigned to BU 1's default team by hand on 2026-10-21 under the same identity. The
    revision was not.

**What this branch's ordinary pass plans for it (report-only):**
- When it walks the matter (recent window or sweep), the rule answers BU 1's default team: the budget first, then the
  matter, both in BU 1.
- The row is user-owned and that team is in another business unit, so the pass reports it ("owned by a user, and the rule
  gives it a team in another business unit: reported, not moved", the I-6 interim) and does not move it. Writing runs
  behave the same.

**Verdict: bad seed data, not a defect.** No product path created or re-owned the row. A product-created revision
(`DecisionActionExecutors` revise-budget) is asked through `ForRow` and owned by the team the rule names. To clean it up,
assign the row to BU 1's default team by hand. It is the same class as the 69 user-owned analyses above.

### V-fix 3: a copy's canonical is a Secure-only parent (coordinator ruling 2026-10-28, as built)

**The rule.** An editable copy's link to the byte-identical canonical it was saved as (`sprk_document.sprk_canonicaldocument`)
counts for secure-if-any only: a copy of Secure content is Secure, and an ordinary canonical never decides the copy's
business unit. It is never the primary and never in `OrderedParentsOf`.

**One definition.** `RecordOwnershipResolver.SecureOnlyLookups` sits beside `CitationLookups` with the opposite rule (a
citation counts for nothing; a Secure-only parent counts for secure-if-any). It is read through `IsSecureOnlyParent` by:
- the writers' question (`RecordOwnershipContext.ForRow`; `RecordOwnershipContext.SecureOnlyParents`);
- the reconciler's question (`ForExistingRowAsync`, through `SecureOnlyParentsOf(row)`; `SecureChildReconciler.ContextFor`);
- the re-file (`ReparentAsync`: the Secure-only columns leave the primary order and go to `SecureOnlyParents`; the F3
  after-set includes them, so keeping a Secure canonical is never read as leaving its secure root).

**The answer.** `SecureDecisionAsync` (the one secure-if-any block, now shared) reads the Secure-only parents' facts with the
ordered ones. An unreadable one is refused (`ParentUnresolved`), as any named parent is. With no other parent, a Secure
canonical answers the Secure team; otherwise the row takes the unfiled answer (the caller's team, or KeepCreator in the
reconciler), never the canonical's business unit. The look-through of a primary parent follows ordered parents only
(`withSecureOnly: false`); the lineage walks read the Secure-only parents too.

**What it removes.** The reconciler's "unfiled copy takes the canonical's business unit" path (task 146's case): the link
was a lineage parent at the lowest rank, so on an unfiled copy it became the primary.

**Writers.**
- *Compose* (`ComposeCreateOnSavePromoter`): the canonical is added to the question under its own column; the upsert is
  owned as that answer says.
- *Office* (option a; `OfficeDocumentPersistence.ResolveEditableContentIdentityAsync`, `DocumentOwnershipOf(...,
  canonicalDocumentId)`): no new DI dependency and no re-own after create. **Deviation from "before its owner question":**
  the identity (quickXorHash) can only be read from the uploaded item, and the owner question runs before the upload so a
  refusal writes nothing. So the first question stays where it is, and a SECOND question, with the canonical, is asked after
  the upload and before the create. Its answer owns the create, which links exactly the canonical that question named (the
  identity is passed into the create, not re-resolved). A refusal of the second question writes no document, removes the
  upload when nothing references it, records the job Failed (`OwnerUnresolved`) and answers `OFFICE_022`. Word `.docx`
  saves are stamped with their own record id, so they never link; only unstampable bytes can.

**Known limit (recorded; no mechanism built, per the ruling).** A diverged copy stays isolated. Graduate-on-divergence
clears `sprk_canonicaldocument` with a plain update, which is no re-file and asks no owner. A copy made Secure by its Secure
canonical therefore stays in the Secure team after its content diverges, until a re-file asks again without the link (the
same outcome as an authorized user copying content out of a Secure record) or the root is unsecured.

### Issues to file (issue-ready text; the coordinator files them)

**K-a: Undetermined rows from bad data block their own root's Unsecure.**
- *Context.* The secure pass cannot ask some rows their writer's question (`ExistingRowOwnership.Undetermined`). Examples
  are a Signal or Decision Record whose subject's core-ancestor stamps are ambiguous (conflicting matter columns), or whose
  core record type is not a core table in the record-type catalog. Such a row moves INTO isolation when the parents it
  stores are secure (F1-b). Any other answer leaves it as it is and marks it Failed.
- *Impact.* On Unsecure, a Failed row makes the child pass incomplete, so `POST …/unsecure` answers 500
  `sdap.unsecure.children_incomplete` on every retry until the data is corrected. The row stays isolated (fail closed).
  Since B4 it no longer starves other roots of the reconciliation job.
- *Proposed fix.* Report these rows with a stable reason code on the Unsecure response (a new
  `failedRecords` list beside `heldRecords`; today only the per-table `failed` count shows them). Add an admin repair path, or a report-only list in the census job, so an operator can correct the
  stamps or catalog row. Decide with the owner whether Unsecure may complete with such rows held in isolation (as A2 does
  for held Decision Records).
- *Acceptance.* A root with one undetermined child: Unsecure reports the row and its reason. After the data is fixed, the
  next Unsecure completes.

**K-b: The ordinary-moves time slice does not bound a single root's walk.**
- *Context.* `SecureChildReconciliationJob.OrdinaryTimeSlice` (60 s) is checked between roots. One root's walk (one query
  per lineage table per level, plus resolver reads per row) runs to the end, bounded only by `MaxOrdinaryMovesPerRun`
  (writes) and the root's size.
- *Impact.* A very large ordinary root can make one run long. Other roots are not starved (B4: least-recently-attempted
  order, at least one root per group per run), but that run's report and the next tick are delayed.
- *Proposed fix.* Check the slice between lineage levels or between pages of a root's walk, and carry the root
  (Incomplete) when the slice is spent mid-walk. The carry and the least-recently-attempted order already resume it.
- *Acceptance.* A root whose walk exceeds the slice stops at the next level boundary, is carried, and completes over later
  runs. The slice holds within one level's cost.

**K-c: `RecordCreationService` Repair has no live caller.**
- *Context.* `RecordCreationService.RepairAsync` and its owner step `RepairOwnerAsync` (field-mapping-server-write-path-r1
  task 016; `RecordCreationService.cs` ~:878–897) are not reached by any endpoint or job. R4 fixed its owner question (the
  table's order through `ForRow`, the attribute-order defect above), but nothing exercises it in production.
- *Impact.* Code and tests to maintain with no behaviour behind them. A later caller would inherit rules (task 016's
  fail-closed cross-BU escalation, the mapping gate) that were never run live.
- *Proposed fix.* field-mapping decides: wire Repair to its intended route (FR-09), or delete it and its tests.
- *Acceptance.* Either a live caller with an integration test, or the code removed and the census and guard entries
  updated.

**K4: An undetermined Signal or Decision Record blocks its root's Unsecure.**
- *Context.* A Signal or Decision Record whose question cannot be asked because a READ faulted (a catalog or stamp read,
  the To Do assignee check), as opposed to bad data (K-a), is Failed in the secure pass unless its stored parents are
  secure. This is narrower since A1/A2: a Decision Record with an incomplete `resolvedSignalIds` question is held in
  isolation and reported, not failed.
- *Impact.* The root's Unsecure answers `sdap.unsecure.children_incomplete` until a retry finds the read healthy. The row
  stays isolated (fail closed).
- *Proposed fix.* Retry the faulted reads once inside the pass. If they still fail, report the row as held with a
  retryable reason, so the caller sees "retry" rather than a generic incomplete.
- *Acceptance.* An injected one-off read fault on a root's Signal: Unsecure completes on the in-pass retry. A persistent
  fault: Unsecure reports the row as retryable, and nothing leaves isolation.
