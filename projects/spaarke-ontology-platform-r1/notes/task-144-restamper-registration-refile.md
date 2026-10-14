# Task 144: Signal and Decision Record register with the core-ancestor restamper (2026-10-14)

Branch `task/ontology-144-restamper-refile` (worktree `C:\wts-144`), base `docs/ontology-platform-design`. The base was merged
in at `7015ad9ae`; it brings `a14349173`, the System.Security.Cryptography.Pkcs/Xml 10.0.13 bump. Rigor: FULL (bff-api,
security, dataverse, testing).

## Commits

| Commit | What |
|---|---|
| `68bc79a1b` | The `StampedOutsideAccessTaxonomy` exemption (uac-r2 option A). Its own commit; uac-r2 reviews it with the PR. |
| `e57b33ff7` | The registration: `StampSourceColumns["sprk_signal"]` and `["sprk_decisionrecord"]`. |
| `9824bbacc` | `ReconciledEmailFiling` re-stamps what is filed under the communication it files. The word-add-in team reviews this file. |
| `be187b43b` | Per-path tests for criteria 1–4, plus additive test-double extensions. |
| `debdb39de` | Suppression: the hit probe carries the core's stamp column (uac-r2 #1746 K7), plus the K4/K7 tests. |
| (review fixes) | Report-only tables in the job; the full-lineage guard; the Office-path fault isolation; wording. See "Review dispositions". |

## Acceptance criteria

1. **M → secure X through the BFF route.**
   - Tests: `SecureChildOwnershipAiToolTests.OntologyRestamp_CommunicationMovedUnderASecureMatter_*`, through the communication
     filing route (the one re-file core).
   - Signal and DR end up owned by the Secure Record owner team and stamped X, in the same operation.
2. **The same on the other two paths.**
   - `dataverse.update_record`: the same theory, `update-tool` row.
   - `ReconciledEmailFiling`: `ReconciledEmailFilingTests.FileIfUnfiled_UnderASecureMatter_ReownsAndRestamps*`.
   - A move BACK cannot happen on the Office path, because only an unfiled communication is filed there.
   - The Office re-stamp is fault-isolated: a re-stamp that throws is logged as a Warning, and the filing, the ownership pass
     and the Signal trigger still run (`FileIfUnfiled_WhenTheRestampThrows_TheFilingStands_AndTheTriggerStillRuns`; the fault is
     forced through the restamper's logger, the one way out of its never-throws contract; fails on the pre-fix file). An
     incomplete re-stamp does the same (`..._WhenTheRestampFails_...`). A failed filing write re-stamps nothing
     (`FileIfUnfiled_WhenTheWriteFails_RestampsNothing`; an ordering pin, green on both sides).
3. **Back to M gives M's business-unit team.**
   - Tests: `OntologyRestamp_CommunicationMovedBackToTheOrdinaryMatter_*`, both routes. The mirror is removed too.
   - The release happens only because the stamp followed. The secure-precedence control
     (`OntologyRestamp_WithoutTheRestamp_AStaleSecureStampKeepsTheRowIsolated`) shows that a stale secure stamp keeps the row
     isolated.
4. **The decision context is byte-identical.**
   - Typed `sprk_matter`, `sprk_corerecordid`, `sprk_corerecordtype`, `sprk_policyversion` and `sprk_snapshot` are compared
     before and after each re-file.
   - Every stamp write on these rows touches stamp columns (`CoreAncestorLookups`) only. The test world's `Update` stays
     strict; only the restamper double routes stamp-only writes to `SecureChildShareWorld.ApplyStampWrite`, which refuses any
     other column.
   - The Decision Record is now "append-only except the live stamp columns (task 144, C2)". `DecisionRecordWriter`'s remarks and
     `DecisionRecordContextImmutabilityTests`' messages say so. The D-34 context pins are unchanged and still green.
5. **AMENDED by the coordinator (2026-10-14, uac-r2 option A).** The criterion was "CoreAncestorResolverTests unchanged". It now
   reads: "IntermediateRootColumns and its TypeScript-parity tests unchanged; the three guards change only to read the named
   exemption".
   - `IntermediateRootColumns` and the TypeScript-parity tests are unchanged.
   - `StampSourceTopology_IsClosed`, `CoreAncestorStampTopologyLockstepTests` and `ChildRecordContainerResolutionTests.StampFreshness`
     each read `StampedOutsideAccessTaxonomy` and call `StampedOutsideAccessTaxonomyPin.AssertPinned()`.
   - `StampFreshness_ExemptionIsPinned` compares the covered tables against an independent literal list
     (`sprk_todo, sprk_event, sprk_communication, sprk_memo`), not against a set derived from the code under test.
6. **Report-only stale-row report.** See "Report-only" below.
7. **K4 — not proven by a test; a characterisation only.**
   - `SignalSuppressionTests.Characterisation_AStampOnTheSignal_IsALineageLookupTheWalkFollows_ToAProjectOrWorkAssignmentCore`
     shows that the suppression walk follows a stamp column on a Signal. It seeds the stamp by hand.
   - It is **not** acceptance-7 evidence. The walk (`SecureRootsAboveAsync`) follows the subject's own lookups whatever the
     subject's owner. Any subject whose root the restamper can copy onto the Signal already carries that root as a lineage
     lookup, so the walk reaches the root through the subject without the stamp. In this model a restamper-produced stamp cannot
     be shown to be load-bearing for K4.
   - K4's real closure is K7 (below) plus the walk itself. Reported to the coordinator as a limitation.
8. **K7.** `SignalSuppressionTests.K7_*`: a hit on a secure project or work-assignment core is placed Secure, never Ordinary.
   - A must-not-fire control checks that an ordinary project core stays Ordinary.
   - The probe change is the K7 fix (`SignalSuppressionService.ItemOf(policyCode, hit)`).
9. **Test scope.** Beyond the contract and these cases:
   - `SignalAboutAToDo_FollowsTransitively`: a To Do is a real Signal subject (Do lane), and it is itself a stamped child, so
     the stamp reaches the Signal through one more hop.
   - The job's report-only tests (one shared seed, `StaleOntologyWorld`): the uac-r2 condition "report-only first", per table.
   - The fail-closed and secure-precedence seeds: ADR-038, once each, as the brief asks.

## uac-r2 option A conditions

1. **One source.** `CoreAncestorResolver.StampedOutsideAccessTaxonomy` plus `StampedOutsideAccessTaxonomyReason` and
   `IsStampedOutsideAccessTaxonomy`. The guards contain no per-table `continue`.
2. **Pinned both ways.** The set is exactly the two tables, and each member is in `StampSourceColumns`. Neither member is in
   `ChildRecordEntities` or the TypeScript `CHILD_RECORD_ENTITIES`.
   - Commit 1 is intentionally red on the four "every member is stamped" pins. This is the stale-exemption control firing.
3. **The safety claim is a guard.** `StampedOutsideAccessTaxonomyGuardTests` fails if any `StampSourceColumns` source,
   `IntermediateRootColumns` entry, `RecordContainerResolver.ChildAncestorLinks` entry, or lookup of **any**
   `SecureChildLineage.Children` table targets a member.
   - One allowlisted edge, with its reason: `sprk_signal.sprk_decisionrecord → sprk_decisionrecord`. A Signal's resolving
     Decision Record is one more ownership parent (secure-if-any), so the edge only narrows. `AllowedLineageEdges_AllExist`
     fails if the allowance goes stale. Adding an edge needs uac-r2's review.
   - Must-fire control: an injected todo→signal or event→DR link, a NEW lineage edge from a non-CHILD table
     (`sprk_spendsignal.sprk_signal`), and the allowlisted column on another table (`sprk_todo.sprk_decisionrecord`) all turn
     it red.
   - Must-not-fire control: the real topology, and links FROM a member to its subject, stay green.
   - The reason text now says: "nothing in the stamp or storage topology is filed under them ... and the only lineage edge into
     one is Signal to Decision Record, which only narrows".
4. **`sprk_analysis` stays separate**, as its own exemption in StampFreshness.
5. **The stamp is never an access input for these two tables, and the re-own fails closed.** This is stated in the reason text
   and in the commit message.
   - **Refused re-own into a secure parent (condition 5 wording, agreed with uac-r2).** A refused re-own into secure keeps the
     previous owner until the reconciler succeeds. A persistent refusal fails every 2-minute run: the root stays in
     `incompleteRoots` with `Success=false`, and `AfterRefileAsync` logs an Error after its second attempt.
   - The reason text says what holds: a refusal "never puts a row on a wider owner" (a refused release out of isolation leaves
     the row isolated). The move-in residual is uac-r2's existing window. 144 adds no new one.

## Window trace (coordinator's question, answered before the ReconciledEmailFiling change)

- **No stamp: fails closed.**
  - The owner follows lineage: `AfterRefileAsync` moves the child to the Secure Record owner team in the same operation.
  - Storage answers 409 `container_ancestor_stale`.
  - The external lists do not match a null stamp.
- **Stale stamp: readable.** A communication on ordinary M is unfiled outside the BFF, then filed under secure X by an Office
  save within the 5-minute window. Its children keep M's copy.
  - External M grantees can read them through `ExternalProjectDataEndpoints.cs:416/420` → `ExternalDataService.cs:696`. That read
    is app-only and filters on `_sprk_regardingmatter_value eq M`.
  - The events list (`:868` / `:780`) has the same shape.
  - This was handed to uac-r2 as a security F (owner: uac-r2). 144 did not file it and did not change the external-access code.
  - Signals and Decision Records have no external module.
- **144 closes only the Office-save case.** `ReconciledEmailFiling` now re-stamps in the same operation, as the filing route and
  the update tool already did. Every other out-of-BFF window is still covered only by the 5-minute job.

## Report-only (criterion 6)

### The job: per-table report-only list

- New setting `CoreAncestor:StampReconciliation:ReportOnlyTables` (comma-separated). **Absent = `sprk_signal,sprk_decisionrecord`**;
  an empty value = no report-only tables.
- With writes ON (the job's default, `WritesEnabled` absent = true), a stale row of a report-only table is scanned, counted
  (`reportedOnly`) and sampled (`sampleStale`), and **never written**: not as a direct repair, and not by the cascade below
  another row the job repairs (`RestampChildAsync(..., holdTables)` skips those child tables). Every other table keeps repairing;
  the storage resolver's 409-stale recovery depends on that.
- The BFF's own re-file paths (filing route, update tool, `ReconciledEmailFiling`) re-stamp Signals and DRs regardless: they
  pass no hold list. That is the contract this task adds.
- Turning the job's writes on for Signals and DRs is an **owner decision**: set the key to an empty value. Use JSON
  configuration or an App Service setting with an explicit empty value; some hosts drop an empty environment variable, which
  reads as "absent" and keeps the defaults (fails closed).
- A setting that names a table the job does not stamp (a typo such as `sprk_signals`) **fails closed**: the default holds are
  kept as well, a Warning is logged, and the run reports `partial` until it is corrected
  (`ReportOnlyTables_WithAnUnknownTable_KeepsTheDefaultHolds`).
- The heartbeat log line carries `reportedOnly` and `reportOnlyTables`.
- Tests: `ReportOnlyTables_ByDefault_ReportTheOntologyRows_AndRepairEveryOtherTable` (Signal and DR),
  `ReportOnlyTables_WithAnUnknownTable_KeepsTheDefaultHolds`,
  `ReportOnlyTables_Empty_RepairsTheOntologyRows` (must-fire control), `WritesOff_ReportsTheOntologyRow_AndWritesNothing`.

### The read-only run (spaarkedev1, 2026-10-14)

The job with the registration is not deployed, and deploying it is a dev change. So the job's classification rule was applied as
read-only queries (Dataverse MCP `read_query`, org `spaarkedev1` `0c3e6ad9-…`). This is an equivalent of the run, not the deployed
job.

| Query | Result |
|---|---|
| `sprk_signal` rows | 0 |
| `sprk_decisionrecord` rows | 0 |
| Stamp columns and source columns (`sprk_regarding{matter,project,workassignment,servicerequest,communication,document,event,invoice,todo}`, `sprk_regardingrecordid`) exist on both tables | yes (the select resolves) |

**Stale rows: none.** There is nothing to repair, and nothing to hand uac-r2 beyond this.

## Side effects and hazards (review K-items, recorded for pickup)

- **To-do cascade.** A To Do is a stamped child AND an intermediate for Signals about it. A BFF re-file whose cascade reaches a
  To Do now also re-stamps the Signals about that To Do (one hop further down). The job does not: it holds report-only tables
  out of its cascade.
- **Decision Record volume.** Each DR is now a stamped row: the job reads every DR with an intermediate subject every 5 minutes
  (bounded at 1,000 rows/page × 20 pages per table, cursor between runs), and every re-file cascade reads the DRs under the
  re-filed row. DRs only grow (append-only), so this read grows without bound. Dev has 0 rows today; revisit the job's
  low-volume placement note (D-61) when DRs reach the thousands.
- **No stamp at Signal create.** `SignalWriter` writes no stamp ("the Signal's live stamp belongs to the restamper
  registration"). A new Signal is unstamped until a re-file cascade reaches it, or until the job stamps it (up to 5 minutes) —
  and **indefinitely while Signals are report-only in the job**. An unstamped Signal fails closed for storage and the external
  lists; K7 holds regardless because the probe models the stamp. #1854 owns `SignalWriter`; not changed here.
- **"The stamp follows the subject, not the core."** A Signal or DR about an intermediate (a communication, a To Do) carries the
  core of its subject's CURRENT filing, which is not necessarily the core the decision was made about (D-34 context). A consumer
  that reads the stamp as "the decision's core" is wrong after a re-file. The coordinator added this to the 145 POML.
- **Office-path fault.** A re-stamp fault on the Office path no longer skips the ownership pass or the Signal trigger, and it no
  longer logs "left as it was"; the job re-stamps the rows within one cycle (the non-report-only ones; Signals and DRs only once
  their writes are on, or on the next BFF re-file).

## Review dispositions (two reviews, 2026-10-14)

| Finding | Class | Disposition |
|---|---|---|
| The job would write Signals/DRs before uac-r2's "report-only first" is met | F4 | Fixed: per-table `ReportOnlyTables`, default Signal + DR, held out of the cascade too; tests above. |
| The K4 test seeds the stamp by hand, so it proves nothing | F3 | Relabelled as a characterisation test; dropped from acceptance-7 evidence; reasoning in criterion 7. |
| `SecureChildShareWorld.Update` accepted stamp writes (weakened the strict double) | F3 | Fixed: `Update` strict again; stamp writes go through `ApplyStampWrite` from `RestamperOver` only. |
| The guard scanned only CHILD-taxonomy lineage rows | F4 (security) | Fixed: every `SecureChildLineage.Children` entry; Signal→DR allowlisted with a reason; must-fire for a new edge. |
| The reason text claimed "nothing is filed under them" while a lineage edge exists | F3 | Reworded (condition 3 above). |
| `StampFreshness_ExemptionIsPinned` compared `covered` with a set derived from the code | F3 | Fixed: an independent literal list. |
| A re-stamp fault on the Office path skipped the ownership pass and the trigger | K | Fixed: a local try/catch; negative test. |
| "Append-only" wording on the DR writer and the D-34 ArchTests | K | Amended to "append-only except the live stamp columns (task 144, C2)". |
| To-do cascade, DR volume, no stamp at create, stamp follows the subject | K | Recorded above. |
| Stale stamp readable through the external To Do / event lists | F (security) | Not filed by 144, by instruction; uac-r2 owns it. |
| Re-check: a typo in `ReportOnlyTables` silently releases the hold | K2 | Fixed: fails closed (defaults kept, Warning, run `partial`); test. |
| Re-check: `reportedOnly` missing from the heartbeat log | K3 | Fixed: heartbeat carries `reportedOnly` and `reportOnlyTables`. |
| Re-check: an empty environment variable may be dropped by the host | K3 | Recorded above (set it in JSON or as an explicit empty App Service setting). |

## BFF hygiene

- **Placement (§10).** No new endpoint, service, DI registration or package.
  - `ReconciledEmailFiling` takes the already-registered `CoreAncestorRestamper`; `OfficeModule` resolves it.
  - The registration is two dictionary entries in the existing `StampSourceColumns`.
  - The K7 change is five lines in an existing private method.
  - The report-only list is one configuration key on the existing job and one optional parameter on the existing
    `RestampChildAsync`.
- **§11 (new surface).**
  - `StampedOutsideAccessTaxonomy`. *Existing:* `ChildRecordEntities` is the access taxonomy, the wrong set. *Extension:* adding
    the two tables there would make them CHILD access records, which uac-r2 refused. *Cost of doing nothing:* three uac-r2 guards
    fail on the registration.
  - `CoreAncestor:StampReconciliation:ReportOnlyTables`. *Existing:* `WritesEnabled`, which is all-or-nothing. *Extension:*
    turning `WritesEnabled` off would stop the repair the storage resolver's 409-stale recovery depends on. *Cost of doing
    nothing:* the job writes Signals and DRs before uac-r2's "report-only first" condition is met.
  - `SecureChildShareWorld.RestamperOver` / `ApplyStampWrite`: test-only. *Existing:* `CoreAncestorsOver`, which has no
    single-row read, and the strict `Update`. *Cost of doing nothing:* the re-own could not read the new stamp in a test.
- **Identity.** The restamper writes Signal and DR stamps under the BFF's own identity, the one uac-r2's reconcile already uses
  to re-own those rows. The ontology writer identity (`OntologyWriterDataverseClient`) still has no Write on the DR table.
- **Publish size:** see the PR body (re-measured after the review fixes against a fresh build of the new base `a14349173`).
- **No new packages.** No new HIGH CVE (no package change).

## Files of other lanes

- `CoreAncestorResolver.cs`, the uac-r2 guards, `SecureChildShareWorld.cs` and `FakeRecordShareTable.cs` belong to uac-r2. uac-r2
  reviews them with the PR.
- `ReconciledEmailFiling.cs` belongs to word-add-in-r1. The PR body flags it.
- No #1854 file was touched (`SignalWriter`, `SignalReevaluationJob`, `SignalClosureService`, `SignalEvaluationTriggerHandler`,
  `SecureChildReconciler`).
- No second re-own path was added: the re-own is `SecureChildReconciler.AfterRefileAsync`.

## Known limits (uac-r2 review, 2026-10-14)

uac-r2 verdict: **approve**. All 5 conditions hold; no security F; targeted 771/771, ArchTests 973/973.

- **K (accepted by uac-r2): the report-only hold covers the reconciliation job only.**
  `CoreAncestor:StampReconciliation:ReportOnlyTables` holds writes only inside the reconciliation job. Two other entry points
  write Signal and Decision Record stamps while those tables are report-only:
  - `CoreAncestorRestamper` called via `AfterWriteAsync` (8 write paths, including `ReconciledEmailFiling.cs:148`).
  - The 409-stale queue handler, `CoreAncestorRestampJobHandler.cs:54`.
  - `OntologyStampedChildRestampTests.cs:110` pins this.
  - There is no access impact: for these two tables the stamp is only another ownership parent, and a secure parent wins.
- **K2 (closed): the depth >= 2 hold in the cascade is now covered.**
  `CoreAncestorStampReconciliationJobTests.ReportOnlySignal_TwoHopsBelowTheRepairedRow_IsHeld_AndReported` repairs a stale chain
  event -> to-do -> Signal with Signal report-only: the to-do is written, the Signal is held, and the job's next scan reports it.
  - Seeding proof: removing the `holdTables` argument from the recursive `CascadeAsync` call (`CoreAncestorRestamper.cs`, in the
    depth + 1 cascade) failed exactly this test (Signal written: expected matter A, found B) and no other of the 51 in the two
    restamp classes; restoring it passes.
- **K3 (closed): the `ReconciledEmailFiling` tests now prove `AfterRefileAsync` ran.**
  The two "re-stamp throws / does not finish" tests assert the secure-child re-own of a Signal filed under the communication
  (table, id and the Secure Record owner team) instead of relying on `Reparents.ContainSingle()`, which the filing's own
  reparent satisfies.
  - Seeding proof: replacing the `AfterRefileAsync` call with a no-op failed three tests (these two and the secure-matter test);
    restoring it passes.
