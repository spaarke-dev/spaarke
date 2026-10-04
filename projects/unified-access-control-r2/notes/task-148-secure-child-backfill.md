# Task 148 — existing children follow the record: provisioning, unsecure and the backfill (C10 part 2, #1070)

> **Date**: 2026-10-04 · **Branch**: `task/uac-r2-148` · **Rigor**: FULL (opus, high)
> **Base** (HEAD right after the merges): `e94a8bc2d` = `task/uac-r2-149-r4` + `task/uac-r2-133-c1-r3.-r2` +
> `work/unified-access-control-r2` (`734d05f26`, owner rounds 1-20). Both merges were clean (no textual conflict); the build
> was clean. **One semantic merge conflict** surfaced in the ArchTests and is resolved here (§6).
> **Status: code complete, NOT deployed.** Live steps are manual gates for the main session (§8). Ships with (after) 146 +
> 149; it is the **ship gate** for unsecuring in shared environments (owner round 11 item 3).
> **Owner decisions that bind this task:** round 13 item 1 (unsecure re-owns the SharePoint cascade rows by the 146 rule);
> C10 part 2; round 6 / R6 (secured child roots stay secure, no unsecure cascade); round 10 item 4 (reuse task 133's
> snapshot-and-restore primitive, do not fork it); round 11 item 3 (148 re-owns the children, then calls `SyncRootAsync`;
> the one-time backfill is a script with dry run / verify, `-Apply` not run live by the executor). Tasks 156 and 158 are
> separate and were not touched.

## 1. Outcome

ONE engine, `Services/Access/SecureChildReconciler.cs`, brings every EXISTING child of ONE root (project, matter, work
assignment) into the state task 146's rule gives it, called from three places:

| Trigger | Where | What |
|---|---|---|
| Provisioning | `ProvisionProjectEndpoint` **Step 8** — after the root is isolated (Step 5), shared (Step 5.5 + colleagues) and has its container (Steps 6/7, or the one it keeps); task 149's former Step 8 (`SyncRootAsync`) is folded in | every child re-owned INTO `Secure Record Owners` (read back), then `SyncRootAsync` mirrors the root's sharees. Incomplete → **500 `sdap.provision.children_incomplete`** (counts per table); the record IS provisioned, so calling again goes to the already-provisioned branch (below), which completes it. After the container on purpose: a related record the rule cannot place (a refusal) never keeps the record from storing documents. (A first draft ran the pass before the container and resumed through 133's RESUME path; moved after review — one re-entry point, and storage never blocked by a child) |
| Provisioning, already provisioned | the 409 branch | the same pass first: nothing to do → **409 `already_provisioned` unchanged**; work done and complete → **200 `childrenOnly: true`** (`sharedToCreatorSystemUserId` = empty GUID — no share was written by this call); incomplete → 500 `children_incomplete` |
| Unsecure | `UnsecureProjectEndpoint` **Step 2.5** (cascade rows read before the move — `sdap.unsecure.cascade_children_unreadable` refuses before any write, task 133's handoff) and **Step 3.5** — after the root's move (Step 3), BEFORE the root's shares are revoked (Step 4) and the flag cleared (Step 5) | every Secure-team-owned child re-owned OUT to the owner the resolver gives a child of the now-ordinary root (its business unit's team), THEN its mirrored shares removed; the SharePoint location / document rows the root's Assign cascaded to the new owning USER are placed by the same rule (owner round 13 item 1). Incomplete → **500 `sdap.unsecure.children_incomplete`**: the root's shares are NOT revoked and `sprk_issecure` is NOT cleared; calling again completes it. On a record already not secure, the call completes the children an earlier (pre-148) unsecure left isolated |
| Sweep | NEW `SecureChildReconciliationJob` (`secure-child-reconciliation`) | every `sprk_issecure = true` root of all three types, ordered (table, id), capped (`SecureChild:Reconciliation:MaxRootsPerRun`, default 50), resumable: the next run continues after the last root the previous one reached — a cursor in the job singleton, per instance (task 143's `NoAccessShareReconciliationJob` precedent; a first draft read it back from the scheduler's run history, which `WorkloadPlacementGuardTests` — ADR-052 §5 / ADR-036 A1 rule 7 — rightly refused: a job must not depend on the scheduler's store). Each run's `ResultJson` (`resumeAfter`, `passComplete`, totals, the first 200 changes with previous owners) and a progress log line per root show the position. **Registered DISABLED** and **REPORT-ONLY** unless `SecureChild:Reconciliation:WritesEnabled` = true. The one-time backfill is `scripts/Invoke-SecureChildBackfill.ps1` (dry run default / `-Apply` / `-Verify`), which only triggers the job and reads its reports — through the admin run history, which now carries each run's `ResultJson` (`JobRunDetail.ResultJson`, additive; FR-2.8 designed it for the admin surface) |

**What the engine does, precisely.**
1. Resolves the Secure Record owner team (the synchronizer's rule — no team → NotApplicable), then reads the root.
2. Walks the root's DESCENDANTS downward through task 149's lineage map (`SecureChildLineage`: direct root links incl.
   `sprk_relatedproject`, the FR-26 `sprk_regarding{core}` stamps, and every further hop — a to-do filed only under a
   document), through rows of ANY owner (a not-yet-secured child is ordinary-team-owned), ≤ 6 levels, every column (the
   resolver reads a row's parents from whatever lookups it carries), paged with a ceiling. A fault → Failed, nothing written.
3. Places the platform-cascade rows (task 133's `AssignCascadeChildOwners`: snapshot, then its restore aimed at the
   resolver's owner for a child of the root — read, assign only when different, read back).
4. For each descendant, SHALLOWEST FIRST: the resolver's answer for the row's own parents — `RecordOwnershipContext.ParentsOf`
   over all its columns (the reparent's input), plus its record thread's filing for a message (S6), `WhenUnfiled =
   KeepCreator` (a row naming no parent keeps its owner), and, mid-unsecure only, `UnsecuringRoot` (§3). A row is moved
   ONLY across the isolation boundary (into the Secure team, or out of it); an ordinary row the rule would give another
   ordinary team is untouched. Each move: a log line naming the previous owner FIRST (reversal evidence), its own update,
   a read-back (a write reported failed may have landed — the read decides).
5. Into isolation: `SyncRootAsync(root)`. Out of isolation: `RemoveMirrorAsync` on each child that left — every direct
   share for a child that WAS Secure-team-owned (each was the synchronizer's mirror), the root's sharees' shares only for a
   child found already outside (a resumed unsecure, or a child the root never isolated).
6. Reports per table (examined / already correct / re-owned / would re-own / untouched / refused / failed), every changed,
   planned, refused or failed row with its previous owner, the share sync and the mirror removals.

## 2. Ordering: the constraint's fallback order is the one shipped (recorded, per the constraint)

The ordering constraint asks for **mirror → re-own** on provisioning, "if 149 Part A shows shares do not survive Assign, the
order becomes re-own → mirror and the transient under-share (never an over-share) is recorded". Shipped: **re-own → mirror**,
for a reason that holds whatever probe (g) answers: task 149's synchronizer mirrors ONLY rows the Secure team already owns
(that is its scope rule — it never touches an ordinary-team-owned row), and the scope constraint binds shares to "ONLY task
149's reconcile(root) — no second implementation". Mirroring before the re-own would need a second share writer for
ordinary rows. Probe (g) (G149-1 step 4) is still unrun, so the shipped order does not depend on it either way.

**The transient, recorded:** between a child's re-own and the `SyncRootAsync` that follows the pass, the root's sharees
cannot read that child through a share (they read it before through their business unit, if they were in it). An
UNDER-share for the length of the pass, never an over-share: a re-own only REMOVES readers (the ordinary team's members);
no principal gains a child it could not read before. With `organization.sharetopreviousowneronassign` = true (guide §7 5b
says it must be false) the previous owner team would get a share — a principal that already read the row as its owner,
and the sync then revokes it (not in the root's mirror).

Unsecure follows the constraint exactly: root re-owned (Step 3) → children re-owned → child mirrored shares removed → root
shares revoked (Step 4) → flag cleared (Step 5), asserted by call order over one shared sequence.

## 3. The resolver's one new input: `RecordOwnershipContext.UnsecuringRoot`

Mid-unsecure, the root is owned by a user (Step 3, read back) and still flagged (cleared last; kept on an incomplete pass).
Task 146's C11 refusal — "a parent flagged `sprk_issecure` but not isolated REFUSES" — would refuse every one of its
children, so the transition could never re-own them. Options considered: clear the flag first (violates "flag cleared
last" and "a failed child pass does NOT clear the flag"); drop the root from the child's parents (not "the same inputs
146's writers pass"); a second rule in the reconciler (forbidden). Chosen: extend the ONE resolver in place (owner D-1)
with a named input — `UnsecuringRoot`, the one root mid-transition, whose flag is then not read as a failed provisioning;
its OWNERSHIP decides, as for any ordinary parent. Every other parent keeps every rule. Only the reconciler sets it, and
only when the caller says `unsecuring: true` AND the root reads as NOT Secure-team-owned (a root still on the team is
refused the exemption — Failed). Pinned by `RecordOwnershipResolverTests.ResolveOwner_ForTheRootBeingUnsecured_…` (a
second flagged-not-isolated parent beside it still refuses) — seed S4 bites.

The sweep never sets it: a flagged root NOT on the Secure team (a failed provisioning, or an unsecure interrupted after its
move) has its children REFUSED by the sweep (`TheSweep_OverAFlaggedButNotIsolatedRecord_RefusesItsChildren_AndWritesNothing`);
the sweep cannot tell the two apart, and the unsecure call (repeated) is what completes the second.

## 4. Escalation triggers (POML) — none fired

| # | Trigger | State |
|---|---|---|
| 1 | Report-only sweep finds children of a secure root whose content sits in a SHARED SPE container | **Does not fire in dev** (read-only census 2026-10-04, below): dev has ONE secure record (project `65a3fab2-77a5-f111-aaad-70a8a590c51c`, owned by `Secure Record Owners` `6eabc7f9…`, its own container) and **zero** children of any kind (documents by `sprk_project` / `sprk_relatedproject`, events, to-dos, communications, memos, analyses, invoices, threads, child work assignments, SharePoint document locations — all 0), and no secure matter or work assignment. Stays a gate condition for any other environment's §7c.1 step 2 (guide) |
| 2 | Dataverse refuses a re-own naming a privilege task 145 did not grant | Live only (G148-2). The reconciler reports such a row `Failed` with Dataverse's message, never widens a role; the guide §7a row says it is an escalation to the codified role set |
| 3 | A root→child relationship cascades Assign | **Does not fire**: task 149 Part A (live metadata, 2026-10-02) — no Spaarke root→child relationship cascades Assign; only the system `team` / `sharepointdocumentlocation` / `sharepointdocument` relationships do, accepted by owner round 4 item 3 and placed here by round 13 item 1 |
| 4 | Task 133's provisioning change conflicts with this task's resume behaviour | **Does not fire**: 133 (c1-r3.-r2) is merged into this base and its resume path (team-owned, no container → RESUME) is untouched; the child pass runs after the container, so an incomplete child pass leaves a PROVISIONED record, re-entered through the already-provisioned branch, which completes children only (it never re-provisions, writes no share to the record) — one re-entry for the children, none added to 133's |

**Read-only census (2026-10-04, `az` token, GET only)** — script `scratchpad/t148/census.py`; secure roots by
`sprk_issecure eq true`; children by each lineage lookup of the one secure project. Output as above.

## 5. Placement (CLAUDE.md §10) and justification (§11)

**Placement: BFF** (`bff-extensions.md` decision criteria; ADR-052): two triggers run inside the provisioning / unsecure
request (the caller is told, per table, what happened to the children — a sweep elsewhere could not); the third is
low-volume BFF-identity domain work on the in-process scheduler (ADR-052 "BFF, schedule"; ADR-036 `IScheduledJob`, no
hand-rolled `BackgroundService`). No Functions / Container Apps signal applies (no long-running compute, no separate
scaling need, no separate identity). No package; no endpoint; no column; no plugin (ADR-002).

| New surface | Existing (grep) | Extension | Cost of doing nothing |
|---|---|---|---|
| `SecureChildReconciler` (service + 1 Scoped DI line, concrete — ADR-010) | `ProvisionProjectEndpoint` / `UnsecureProjectEndpoint` touch the ROOT row only (grep `sprk_documents\|sprk_events\|sprk_todos`: none); `RecordOwnershipResolver` decides the owner of a row being created or RE-FILED; `SecureChildShareSynchronizer` mirrors shares on rows the Secure team ALREADY owns (its walk never passes an ordinary-team row); `AssignCascadeChildOwners` covers only the platform's Assign-cascade rows; reconciliation jobs (`ExternalAccessReconciliationJob`, `MembershipReconciliationJob`, `SecureChildShareReconciliationJob`) reconcile grant rows, junctions, child SHARES | Not in the endpoints (3 triggers would copy the pass — the L1/L4 drift the write-path architecture forbids); not in the resolver (decides, does not walk or write); not in the synchronizer (owns POA on isolated rows; its scope rule is load-bearing). The reconciler composes the three; no rule is re-implemented | a record made secure after it has children leaves every one readable by ordinary users; an unsecured record leaves its children owned by a memberless team (reachable only through stale mirrors — the round 11 ship gate); every record secure today keeps its children exposed |
| `SecureChildReconciliationJob` (`IScheduledJob` + `AddScheduledJob(…, enabled: false)`) | `SecureChildShareReconciliationJob` (shares only, writes on — it IS 149's mechanism); `ExternalAccessReconciliationJob` (the disabled + report-only precedent) | Folding owner moves into 149's 2-minute share job would give a writes-on, every-2-minutes job a bulk OWNERSHIP write path the owner has not reviewed; the report-only + disabled posture needs its own registration | the records secure before 148 never get their children secured (no backfill), and task 147 has no L4 net to schedule |
| `SecureChildShareSynchronizer.RemoveMirrorAsync` + `ReadRootShareesAsync` (methods on the existing class) | the synchronizer is the ONE child-POA writer; nothing removes a released child's mirror | extend it, so every child POA write stays in one class (and the refusal "still Secure-owned → refuse" lives next to the mirror) | a child moved out of isolation keeps its former sharees' shares indefinitely (invisible access) |
| `RecordOwnershipContext.UnsecuringRoot` (one property on the existing context) | the resolver's C11 refusal | extend in place (§3) | unsecure can never re-own a child (every one refused) |
| `SecureChildPassSummary` DTO + `Children` / `ChildrenOnly` on the two responses (additive JSON) | none report child outcomes | — | the caller cannot tell whether the related records moved |
| `JobRunDetail.ResultJson` on `GET /api/admin/jobs/{id}/history` and `/status` (additive; SystemAdmin only) | `JobRunResult.ResultJson` is persisted for "the admin UI / history queries" (FR-2.8) but no route returned it | extend the existing run DTO — one optional member, one mapping line | the backfill's dry run / verify could not read a run's report without App Service log access |
| Reason codes `sdap.provision.children_incomplete`, `sdap.unsecure.children_incomplete`, `sdap.unsecure.cascade_children_unreadable` | the 133 / 149 `*_incomplete` / `cascade_children_unreadable` precedents | — | an incomplete pass would be a silent 200 or a bare 500 (ADR-003) |
| Options `SecureChild:Reconciliation:WritesEnabled` (positive name, absent = report-only) and `:MaxRootsPerRun` (default 50) | the `ExternalAccess:` / `IdentityLink:Reconciliation:WritesEnabled` precedent | — | the backfill could not be reviewed before it writes; an unbounded run on a large environment |
| `scripts/Invoke-SecureChildBackfill.ps1` | `Backfill-CoreAncestorStamps.ps1` / word-add-in's `Backfill-RecordOwnership.ps1` compute the rule in PowerShell — forbidden here (constraint "a script, if any, only triggers or reports") | triggers the BFF job and reads its reports; `-Apply` toggles the setting for the run and back in a `finally` | an operator would have to hand-drive the admin API, the app setting and the restart |

## 6. Merge record (the base) — one semantic conflict

`git merge` of `task/uac-r2-133-c1-r3.-r2` and `work/unified-access-control-r2` into `task/uac-r2-149-r4`: no textual
conflict; build clean. ArchTests then failed **371/372**: `RecordOwnerAssignmentCensusTests.EveryOwnerWriteIsCensused` —
"UNLISTED owner write in `AssignCascadeChildOwners.cs.RestoreOneAsync`". Task 133 c1 added that owner write; task 146 r2's
owner-write census (merged through 149) had never seen it — the same shape as the r3 `MoveOwnerAsync` rename conflict.
Resolved: a census entry, kind **Root** (the row is one a ROOT's Assign cascades to — never a `sprk_*` child — placed on its
snapshotted owner, or, task 148, on the resolver's owner for a child of that root). Both intents kept.

## 7. Tests

NEW `tests/integration/data-mutation/ExternalAccess/SecureChildTransitionTests.cs` (**19 cases**) — the REAL endpoints, the
REAL reconciler, resolver and synchronizer, over ONE in-memory Dataverse; the provisioning fixture keeps the root it moves in
step with the world the reconciler reads (`ProvisionProjectTestFixture.UseChildWorldForRoots`). Every family seeds DECOYS —
an unfiled document, a document of another project, a per-user Direct thread (with a message of the record in it), a child
ROOT (a work assignment under the project, flagged) and that root's own to-do — asserted never written.

| AC | Test(s) |
|---|---|
| 1 (+4 decoys) | `Provisioning_ReownsEveryExistingChild_AndMirrorsTheRecordsSharees_AndTouchesNoDecoy` × project / matter / work assignment: two document lookups (project), event, to-do under the document only (grandchild), user-owned communication, memo, message in a Direct thread — all Secure-team-owned (read back), each carrying exactly the creator's mirror (no Share); response per-table counts |
| 2 | `Unsecure_ReownsChildren_ThenRemovesTheirMirror_ThenRevokesTheRecord_ThenClearsTheFlag` × 3 — every child on the BU team, no child shares, the cascade location on the BU team (round 13 item 1), order asserted over one sequence: last child re-own < first child revoke; last child revoke < first root revoke; last root revoke < flag cleared |
| 3 | `Provisioning_WhenAChildCannotBeReowned_IsIncompleteWithCounts_AndASecondCallCompletesIt` (the container exists after the first call; the second answers 200 `childrenOnly`, no second container); `Unsecure_WhenAChildCannotBeReowned_KeepsTheFlagAndTheRecordsShares_AndASecondCallCompletesIt`; `Unsecure_WhenAChildsMirrorCannotBeRemoved_IsIncomplete_AndTheSecondCallRemovesIt`; `Provisioning_WhenAReownIsAcceptedButDoesNotReadBack_IsIncomplete_AndThatChildIsNotMirrored` |
| 4 | the decoys above; `Unsecure_LeavesAChildOfASecondSecureRecordIsolated`; `Unsecure_OnAnAlreadyUnsecuredRecord_CompletesTheChildrenAnEarlierUnsecureLeftIsolated` (a user-owned ordinary child is NOT moved, its share kept) |
| 5 (never over-share) | the provisioning-incomplete test: every grant names only the record's sharees; the refused child keeps its old owner and gets no share; `TheMirrorIsNeverRemovedFromAChildStillIsolated` (the synchronizer refuses, whoever calls) |
| 6 | `Provisioning_RepeatedOnAProvisionedRecord_WritesNothing_ButCompletesChildrenLeftBehind` (409, no owner write, no grant; then a late child → 200 `childrenOnly`); `TheSweep_ReportsOnlyByDefault_AppliesWhenEnabled_AndThenFindsNothingToDo` (third run: zero) |
| 7 | the sweep test (report-only writes nothing and lists each planned change with the previous owner; writes-on applies, reads back, mirrors, reports previous owners); `TheSweep_IsCappedPerRun_AndTheNextRunResumesAfterTheLastRecord`; `TheSweep_OverAFlaggedButNotIsolatedRecord_RefusesItsChildren_AndWritesNothing`. "Registered disabled" is in code (`enabled: false`) — a DI-registration test is banned (ADR-038) |
| 8 | `ACallerWithoutWriteOnTheRecord_Gets403_AndNoChildIsTouched` × provision / unsecure |

Also: `RecordOwnershipResolverTests` +1 (§3); `JobsEndpointsTests` +1 (`GetJobHistory_CarriesEachRunsResultJsonVerbatim` — the backfill script reads the run reports from the admin history; one line of justification beyond the AC's test scope); client jest (`provisioningService.test.ts`) +3 (§9); the census +2 entries (§6).

Test-infrastructure changes (no second harness): `SecureChildShareWorld` gains owner updates (recorded with a shared
sequence; refuse / ignore faults), a derived `owningbusinessunit` (as Dataverse derives it — the resolver reads it), users,
`Standard(secureBu, secureTeam)` so it can carry another harness's ids, and `ReconcilerOver`; `ProvisionProjectTestFixture`
registers the REAL reconciler over its `ChildWorld` and gains `UseChildWorldForRoots` (opt-in mirroring of its roots into the
world). The fixture's default world (no Secure BU) makes the reconciler NotApplicable, so every existing fixture test's
contract is unchanged.

**Seed-and-bite** (each seed made in production code by `scratchpad/t148/seeds.py`, built, the transition + resolver suites
run, the file restored from a byte copy and touched; `git status` clean on `src/` after each):

| Seed | Mutation | Failed |
|---|---|---|
| S1 | the isolation-boundary filter removed (ordinary rows moved too) | 1 |
| S2 | `SyncRootAsync` skipped after the re-own | 6 |
| S3 | `UnsecuringRoot` never passed for the rows | 6 |
| S4 | resolver exemption widened to every flagged parent | 1 |
| S5 | mirror removal skipped | 6 |
| S6 | unsecure ignores an incomplete child pass | 2 |
| S7 | provisioning ignores an incomplete child pass (first placement) | 2 |
| S7b | provisioning Step 8 ignores an incomplete child pass (after the move behind the container) | 2 |
| S8 | the already-provisioned branch runs no child pass | 2 (after the move; 1 before it) |
| S9 | the job always writes | 1 (re-run after the cursor change: 1) |
| S10 | the job ignores its resume point | 1 (re-run against the per-instance cursor: 1) |
| S11 | the walk stops after one level (no grandchildren) | 7 |
| S12 | the re-own read-back ignored | 2 |
| S13 | the cascade rows not placed | 2 |
| S14 | a resumed unsecure leaves ordinary children's mirror | 1 |
| S15 | unsecure revokes the record before its children | 5 |
| S16 | the mirror removed from a child still isolated | 1 |
| S17 | the admin run history drops the run report | 1 |
| S18 | the platform-cascade rows moved without a transition (a repeat unsecure of an ordinary record) | 1 |
| A1 | the reconciler's owner write removed from the census (ArchTests) | 3 |
| A2 | the reconciler writes a hard-coded team instead of the resolution (ArchTests) | 1 |
| C1 | client: `children_incomplete` not classified (jest) | 2 |
| C2 | client: a `childrenOnly` 2xx not accepted | 1 |
| C3 | client: every 2xx accepted without a creator share | 1 |

(The first run's `if (false)` seeds did not compile — CS0162 is an error in this build — and were redone with a
non-constant condition. After the last seed the test project was REBUILT from the restored source before any result
below was taken — the seed script restores the file but its last build is the seeded one.)

## 8. Manual gates (live writes — the main session runs them; owner round 11 approved them at integration)

Order: after G146-1 (done on dev), the joint 146 + 149 deploy, and this task's deploy.

- **G148-1 — probes (dev).** Seed under an ordinary project, matter and work assignment owned by an existing non-admin
  test user: a document via `sprk_project`, a document via `sprk_relatedproject` (project), an event, a to-do regarding the
  document, a communication, a memo. `POST /api/v1/external-access/provision-project {recordType, recordId}` for each.
  Record: each child's `_owningteam_value` = `6eabc7f9-13be-f111-a05b-0022482913fc`; `principalobjectaccessset?$filter=
  objectid eq <child>` shows the creator at mask 23 (no Share); `GET <child>` with `MSCRMCallerID:
  d6f8f439-40bf-f111-a05b-3833c5e9614d` (`uac.child.user@demo.spaarke.com`, Spaarke Business Unit 1, no shares) → DENIED.
- **G148-2 — unsecure (dev).** `POST /api/v1/external-access/unsecure-project` on one of them. Record: children owned by
  the BU default team; no child POA rows; the root's shares gone; `sprk_issecure` false; the order in the BFF log
  (`[SECURE-CHILD-RECONCILE] reassign` lines before the `[UNSECURE]` revoke lines). A Dataverse refusal naming a privilege
  → STOP (trigger 2).
- **G148-3 — backfill (dev), guide §7c.1.** `scripts/Invoke-SecureChildBackfill.ps1` dry run → review (expected: dev's one
  secure record has no children today, so 0 planned) → `-Apply -ResourceGroup <rg> -AppName <bff-app>` → `-Verify` exit 0.
  Then trigger once more report-only: `wouldChange = 0`.
- **G148-4 — cleanup.** Delete every probe (children, roots); record ids and the deletion.

## 9. Client

`@spaarke/ui-components` `provisioningService.ts`: `sdap.provision.children_incomplete` → `interrupted`, retryable, authored
copy (no "try again" — the host's action); a `childrenOnly` 2xx is accepted without a creator share (the fail-closed check
still refuses any other 2xx without one); the response type gains `children` / `childrenOnly`. Jest: see §10.

## 10. Step 9.5 quality gates and results

**Code review** (coverage-first; severity / confidence; each disposition in code unless stated):

| # | Finding | Severity | Disposition |
|---|---|---|---|
| R1 | First draft ran the child pass BEFORE the container: one related record the rule refuses (e.g. also filed under a record flagged secure but not isolated) would keep a secure record from ever getting storage, and resumed through 133's RESUME path | Warning / high | **Fixed**: Step 8 runs after the container; re-entry is the already-provisioned branch only (§1). Seeds S7b, S8 |
| R2 | First draft read the sweep's resume point from the scheduler's run history | Critical (ADR) / certain | **Fixed** — `WorkloadPlacementGuardTests` (ADR-052 §5 / ADR-036 A1 rule 7) failed; the cursor is now a per-instance field, the task 143 job's precedent (path C — comply). Seed S10 re-run |
| R3 | The platform-cascade rows were placed on the rule's owner even when no transition happened (a repeat unsecure of a never-secure record would move its SharePoint rows from the owning user to the BU team) | Warning / high | **Fixed**: they move only into isolation, out of it, or mid-unsecure; decoy + seed S18 |
| R4 | The reconciler mutated its cached row after a re-own "so the next row's decision reads it" — the resolver reads parents FRESH, so the comment was false | Suggestion / certain | **Fixed**: removed; the shallowest-first ordering is what makes a grandchild see its parent's new owner |
| R5 | A child's `sprk_canonicaldocument` (and other document→document links) count as ownership parents, as in `ReparentAsync`, while Compose's create excludes the canonical link | Info / medium | Recorded (handoffs): the effect can only be an under-share (a copy of a secure document pulled into isolation), never an over-share |
| R6 | Mid-unsecure (root moved, flag still set — and kept on an incomplete pass) a NEW child create on the record is refused by the resolver (flagged-not-isolated) until the unsecure completes | Info / high | Accepted: fail closed for a short window; the unsecure's own response says how to finish |
| R7 | `JobRunDetail.ResultJson` exposes every job's report on the admin history | Info / medium | Accepted: SystemAdmin-only routes; FR-2.8 designed ResultJson for exactly this surface; jobs already keep it small and id-only |
| R8 | `SecureChildReconciler.cs` ≈ 650 lines | Info | Cohesive (one pass: walk → decide → assign → share → report); not decomposed (COMPONENT-COMPLEXITY.md) |

No secret, no new route, no CRUD→AI dependency, no package (CVE scan: no vulnerable packages).

**ADR check:** ADR-001 (no new route; handlers extended) ✓ · ADR-002 (no plugin) ✓ · ADR-003 (every unreadable read decides nothing; an incomplete pass is never a success; the unsecure flag stays set) ✓ · ADR-008 (the delegation filter unchanged; the 403 test) ✓ · ADR-010 (concrete classes, no interface; +2 registrations) ✓ · ADR-013 (no AI type) ✓ · ADR-019 (ProblemDetails + stable reason codes + traceId) ✓ · ADR-032 (unconditional registrations; the job's disabled state is scheduler data) ✓ · ADR-036 A1 (IScheduledJob via AddScheduledJob; heartbeat every attempt; throws only when the listing fails; no scheduler-store dependency — R2) ✓ · ADR-038 (no `Mock<HttpMessageHandler>`; no DI-registration test — the "registered disabled" check was written and REMOVED for this reason; no ctor null-check test; data-mutation KEEP path; every guard seeded) ✓ · ADR-052 (BFF, schedule; no BackgroundService) ✓. **No ADR conflict** (no §6.5 path needed; R2 was path C).

**Results** (2026-10-04):

- **Affected suites** (SecureChildTransition, RecordOwnershipResolver, JobsEndpoints, Provision*, SecureProjectShare,
  SecureNamedOwnerTeam, SecureChild*, NoAccessShare*, RecordOwnership*, InternalUserShare*, UnsecureProject*,
  SecureShareNoAccessGuard*, DirectThread*, *ReconciliationJob*): **743/743**.
- **Full BFF unit suite** (`dotnet test tests/unit/Sprk.Bff.Api.Tests`): **14,805 = 14,751 passed + 54 skipped
  (pre-existing) + 0 failed** (20 m 45 s).
- **ArchTests** (`dotnet test tests/Spaarke.ArchTests`): **372/372** — after the census entries (§6) and R2's fix.
- **Integration (project hard gate):** `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **428 = 403
  passed + 25 skipped, 0 failed**.
- **Client** (`@spaarke/ui-components`): `npm run build` (tsc) clean — with the sibling packages `@spaarke/sdap-client` and
  `@spaarke/auth` built in this worktree (`npm install --legacy-peer-deps --no-audit --no-fund` + `npm run build` in each);
  jest `src/components/CreateProjectWizard` **115/115** (7 suites); eslint on the two changed files: clean. Not a PCF, so
  no `build:prod`.
- **CVE:** `dotnet list package --vulnerable --include-transitive` — no vulnerable packages. **Publish size:** not
  measured (instruction).
- **POML:** well-formed XML.

## 11. `.claude/**` edits needed

None.
