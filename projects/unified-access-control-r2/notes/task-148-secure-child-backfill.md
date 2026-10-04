# Task 148 — existing children follow the record: provisioning, unsecure and the backfill (C10 part 2, #1070)

> **Date**: 2026-10-04 · **Branch**: `task/uac-r2-148` · **Rigor**: FULL (opus, high)
> **Base** (HEAD right after the merges): `e94a8bc2d` = `task/uac-r2-149-r4` + `task/uac-r2-133-c1-r3.-r2` +
> `work/unified-access-control-r2` (`734d05f26`, owner rounds 1-20). Both merges were clean (no textual conflict); the build
> was clean. **One semantic merge conflict** surfaced in the ArchTests and is resolved here (§6).
> **Status: code complete, NOT deployed.** Live steps are manual gates for the main session (§8). Ships with (after) 146 +
> 149; it is the **ship gate** for unsecuring in shared environments (owner round 11 item 3).
> **Owner decisions that bind this task:** round 13 item 1 (unsecure re-owns the SharePoint cascade rows by the 146 rule);
> C10 part 2; round 6 / R6 (secured child roots stay secure, no unsecure cascade); round 10 item 4 (verbatim: "133,
> compensation's reverse Assign cascade: coordinate with task 148's child-ownership logic, then snapshot and restore each
> re-owned child's own owner" — 148 reuses 133's primitive for the rows a root's Assign cascades to; r2 §13 item 4 corrects
> the broader paraphrase this line first carried); round 22 (unsecure removes only mirror shares); round 24 (r2: exact dry
> run; no rule-driven widening without F3); round 11 item 3 (148 re-owns the children, then calls `SyncRootAsync`;
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
4. For each descendant, shallowest first and — with writes — REPEATED TO A FIXPOINT (r1, §12): the resolver's answer for
   the row's own parents — `RecordOwnershipContext.ParentsOf` over all its columns (the reparent's input), plus its record
   thread's filing for a message (S6), `WhenUnfiled = KeepCreator` (a row naming no parent keeps its owner), and, mid-unsecure
   only, `UnsecuringRoot` (§3) — against the owners its parents have NOW. A row is moved ONLY across the isolation boundary
   (into the Secure team, or out of it); an ordinary row the rule would give another ordinary team is untouched. Each round
   applies one kind of move: OUT first, and only for rows resting on no row still waiting to move IN; otherwise the INs.
   Each move: a log line naming the previous owner FIRST (reversal evidence), its own update, a read-back (a write reported
   failed may have landed — the read decides).
5. Out of isolation (any trigger): `RemoveMirrorAsync` on each child that left — ONLY a child the Secure team owned when
   the pass BEGAN (owner round 22; every direct share on it was the synchronizer's mirror); a child found ordinary keeps
   every share. A child whose mirror cannot all be removed is put back on the Secure team (r1, §12). Into isolation:
   `SyncRootAsync(root)`.
6. Reports per table (examined / already correct / re-owned / would re-own / untouched / refused / failed), every changed,
   planned, refused or failed row with its previous owner, the share sync and the mirror removals.

## 2. Ordering: two recorded deviations from the provisioning ordering constraint

The constraint reads: "PROVISIONING: root isolated (Step 5) → root shared (Step 5.5) → child shares mirrored (task 149
mechanism) → children re-owned → reconcile(root) verifies → (container steps unchanged)". Shipped differs in TWO places,
each recorded here as a deviation (r1 — verifier items 5 and 6; the first draft recorded the second only as a review fix
and mis-cited the first):

**Deviation 1 — re-own → mirror, not mirror → re-own.** Sanctioned by **owner round 11 item 3** ("148 re-owns the
children, then calls `SyncRootAsync`") — that decision, not the constraint's conditional fallback, is the authority: the
fallback ("if 149 Part A shows shares do not survive Assign, the order becomes re-own → mirror") is conditional on probe (g)
(G149-1 step 4), which is still unrun, so it cannot be what sanctions the order. The engineering reason is the same: task
149's synchronizer mirrors ONLY rows the Secure team already owns (its scope rule — it never touches an ordinary-team-owned
row), and the scope constraint binds shares to "ONLY task 149's reconcile(root) — no second implementation"; mirroring
before the re-own would need a second share writer for ordinary rows.

**Deviation 2 — the child pass runs AFTER the container steps, not before them.** The constraint puts the children before
"(container steps unchanged)". Shipped: Step 8, after Steps 6/7 (review fix R1, §10). Reason: a related record the rule
refuses (e.g. also filed under a record flagged secure but not isolated) would otherwise keep a secure record from ever
getting its own storage, and task 133's compensation path (which owns a failure before/at the container) would have to
learn the child pass — two resume paths, which the resumability constraint forbids. With the pass after the container, an
incomplete child pass leaves a PROVISIONED record whose one re-entry point is the already-provisioned branch. The invariant
the constraint protects is unaffected: children are only ever re-owned INTO isolation by provisioning (an under-share
transient, never an over-share), whatever the position of the pass relative to the container. Not an ADR deviation (no
§6.5 path needed): the ordering is a task constraint; recorded here and in the POML for the reviewer.

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
| `SecureChildShareSynchronizer.RemoveMirrorAsync` (method on the existing class; r1 removed its `principals` parameter and the `ReadRootShareesAsync` method — owner round 22 made them dead) | the synchronizer is the ONE child-POA writer; nothing removes a released child's mirror | extend it, so every child POA write stays in one class (and the refusal "still Secure-owned → refuse" lives next to the mirror) | a child moved out of isolation keeps its former sharees' shares indefinitely (invisible access) |
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

## 12. r1 fix round — verifier items 1-17 (2026-10-04, branch `task/uac-r2-148-r1`, base `00c1602c2`)

Owner decisions applied: rounds 1-13 + **round 22** (2026-10-04, main session under the round-15 directive: on unsecure a
share is removed from a child ONLY if the Secure team owned it when the pass BEGAN; a share on an ordinary never-isolated
child is kept; the set is snapshotted before any re-own). No escalation trigger fired; nothing was left for the owner.

| # | Verifier item | Disposition |
|---|---|---|
| 1 | CRITICAL — unsecure reports Completed while a stamped grandchild whose table sorts before its same-level parent stays isolated with its mirror | **Fixed** — `SecureChildReconciler` now decides rows against their parents' CURRENT owners and, with writes, repeats to a fixpoint: after each round every open row resting (through any chain of rows of the pass — `AncestorsInPass`, which follows the resolver's look-through) on a row that moved is decided again. Writing it surfaced two siblings of the same root cause, both fixed in the same loop: (a) an ordinary row (a user-owned message regarding an event) was pulled INTO isolation behind an isolated parent decided-but-not-yet-released; (b) a row could be released and pulled back. So each round applies ONE kind of move — OUT first, but only for rows resting on no row still waiting to move IN (a move out widens access, so it is made once it is certain), otherwise the INs (narrowing); a row the pass pulled in whose support then left goes back to its start owner; a row asked to move again after that, or still moving at a ceiling of 2N+2 rounds, is reported Failed (left as it is). Tests: `Unsecure_ReleasesAStampedGrandchild_WhoseTableSortsBeforeItsParents` × 5 (analysis→document via `sprk_documentid`, communication→event, agreement→document, analysis→communication, analysis→invoice — each stamped `sprk_regardingproject`, each asserting both rows on the BU team, no shares, status Completed, `alreadyCorrect` 0 everywhere, and the grandchild's mirror revoked before the record's shares); `Unsecure_ReleasesARowKeptIsolatedThroughAUserOwnedParent` (the look-through chain; the message is never written); `Unsecure_NeverReleasesARowWhoseSupportIsMovingIn`; `Unsecure_PutsARowItPulledIntoIsolationBackOnItsOwner_WhenItsSupportLeaves` (back on its USER, not the BU team, share kept) |
| 2 | HIGH — ten fail-closed guards never seeded | Each now has a test that fails when the guard is seeded (table below). **V4 is moot**: owner round 22 removed the code path (an unsecure no longer reads the root's sharees to strip them from ordinary children — `ReadRootShareesAsync` and `RemoveMirrorAsync`'s `principals` parameter are deleted), so there is no unreadable-sharees branch left to guard |
| 3 | MEDIUM — `-Verify` can pass on the tail of a list | **Fixed** — each run's `ResultJson` carries `startPosition` (1-based, where the run began); `Invoke-Pass` counts a pass only from a run that began at 1 (a tail is printed, saved, not counted, and the script carries on to the next run, which begins at 1), requires every counted run to begin where the previous ended (else: "another trigger moved its place — disable the schedule"), an unchanged `rootsTotal`, and the counted runs to add up to it; a report without `startPosition` (a BFF older than the script) is refused. Verified offline by `scratchpad/t148r1/verify-pass.ps1` (the script's own `Invoke-Pass`, extracted from its AST, over scripted run reports: full pass / tail-then-full / cursor moved / total changed / empty / pre-r1 BFF — 6/6), and each of its three checks seeded (P1-P3, each turns a case red) |
| 4 | MEDIUM — single-instance prerequisite only in the script help | **Fixed** — guide §7c.1 opens with a **Prerequisites** block: ONE App Service instance (why; the check `az appservice plan show … --query sku.capacity`, the scale command, autoscale min/max) and the job's schedule disabled (why; the script now refuses a non-contiguous pass); the §7c table row names the per-instance cursor; the script help says the same |
| 5 | LOW — child pass after the container recorded only as a review fix | **Fixed** — §2 Deviation 2 records it as a deviation from the provisioning ordering constraint, with the reason and why the constraint's invariant is unaffected |
| 6 | LOW — re-own → mirror justified by the wrong source | **Fixed** — §2 Deviation 1 cites owner round 11 item 3 as the authority (the constraint's fallback is conditional on unrun probe (g)); the reconciler remarks cite it too |
| 7 | LOW — doc / comment drift | **Fixed** — `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-2: "resumable through a per-instance cursor in the job (never the scheduler's store — ADR-052 §5), each run reporting where it began and where the next continues" (+ the fixpoint and round 22); `SecureChildReconciliationJob` remarks cite §7c.1 |
| 8 | LOW (design) — unsecure strips root sharees' shares from never-isolated children | **Fixed per owner round 22**: mirror removal only for a row owned by the Secure team when the pass began (`start` snapshot, before any re-own; `RowResult.LeftIsolation = written && IsIsolated(start) && !IsIsolated(current)`); `RemoveMirrorAsync` always revokes every direct share and is only ever given such a row. Consequence handled: a released row whose mirror removal fails is PUT BACK on the Secure team (read back) — left out, the next pass would read it as never isolated and keep its former sharees for good. Tests: `Unsecure_KeepsTheSharesOfAChildThatWasNeverIsolated_EvenOneOfTheRecordsSharees` (mirror removed from the isolated child; a Colleague's manual share on a user-owned child kept — both seeded, R22a/R22b); `Unsecure_WhenAChildsMirrorCannotBeRemoved_IsIncomplete_PutsItBack_AndTheSecondCallRemovesIt` (renamed; writes GeneralTeam then SecureTeam; second call completes) |
| 9 | LOW — a row released by a sweep of an isolated root keeps its mirror | **Fixed** — the mirror removal runs for every row that left isolation, whatever the trigger (before `SyncRootAsync`, so a row put back is re-synced). Test: `TheSweep_RemovesTheMirrorOfARowItTakesOutOfIsolation`. **[Superseded by r2 — owner round 24 item 2: the sweep no longer releases any row; that row is held isolated and reported `NeedsF3`; the test is now `TheSweep_NeverReleasesAnIsolatedRow_ItReportsItNeedsF3_AndKeepsItsShares`. The mirror removal still runs for every row an UNSECURE releases.]** |
| 10 | INFO — the already-not-secure branch now writes; task 150's F3 gate must cover it | **Handoff recorded** (below and POML): when task 150's F3 gate (Full Access + creator; owner round 3b F3 / round 10 item 7) is integrated into `UnsecureProjectEndpoint`, it must sit BEFORE the `record.sprk_issecure != true` early return, not only on the transition path — that branch re-owns children out of isolation. Not changed here (150 is not in this base) |
| 11 | VERIFIED OK | No change |
| 12 | AC2 not met | **Met** — item 1 |
| 13 | AC5 not met | **Met** — item 1 (+ round 22, the put-back, "never released and pulled back") |
| 14 | AC3 not met | **Met** — item 1 removes the false Completed; V1/V2/V6/V10 (+ V7/V9) are tested and seeded |
| 15 | AC7 not proven for projects/matters | **Met** — `TheSweep_ReportOnly_OverAProjectOrMatter_WritesNothing_NotEvenItsCascadeRows` × 2 drives the REAL job over the provisioning fixture's world and Web API double: report-only lists the document, the event and the SharePoint location (with its current owner) and writes nothing — no Web API update, no owner, no grant/revoke/modify (V3 seeded) |
| 16 | AC9 live gate | **PENDING** — G148-1..4 (§8), main session; unchanged |
| 17 | AC11 publish size | **NOT MEASURED** (instruction). CVE scan below |

**Item 2, guard by guard** (each seeded in production code by `scratchpad/t148r1/seeds.py` — one mutation, build, the
named suite, file restored from its bytes and touched; `git diff --stat` identical before and after every batch):

| Seed | Guard removed | Test(s) that fail |
|---|---|---|
| V1 | unsecure Step 2.5 refusal (`cascade_children_unreadable`) | 2 — `Unsecure_WhenTheRowsItsMoveCascadesToCannotBeRead_RefusesBeforeAnyWrite` × (503 unreadable, 403 refused) |
| V2 | unreadable descendant set → Completed | 2 — `ATransitionWhoseRelatedRecordsCannotBeRead_IsFailed_NeverASuccess` × (unsecure, provision) |
| V3 | report-only writes the cascade rows | 2 — `TheSweep_ReportOnly_OverAProjectOrMatter_WritesNothing_NotEvenItsCascadeRows` × (project, matter) |
| V5 | "unsecuring but root still isolated" refusal | 1 — `TheReconciler_RefusesToUnsecureTheChildrenOfARecordStillIsolated` |
| V6 | already-not-secure branch ignores an incomplete pass | 2 — `Unsecure_OnAnAlreadyUnsecuredRecord_WhoseStrandedChildCannotBeReowned_IsIncomplete`, `…_WhoseCascadeRowsCannotBeRead_IsIncomplete` |
| V7 | reconciler cascade-snapshot fault ignored | 1 — `Unsecure_OnAnAlreadyUnsecuredRecord_WhoseCascadeRowsCannotBeRead_IsIncomplete` |
| V8 | the job lists every root | 3 — `TheSweep_ListsOnlyTheRecordsFlaggedSecure` (+2 sweep tests whose worlds hold an ordinary root) |
| V9 | resolver read fault counted Untouched | 1 — `TheSweep_WhenARowsOwnerCannotBeDecided_CountsItFailed_AndTheRunIsNotASuccess` |
| V10 | `IsComplete` treats Failed as complete | 5 — the two `ATransition…` cases, `TheSweep_WhenTheRecordOrTheSecureTeamCannotBeRead_IsFailed_AndWritesNothing` × 2, `TheReconciler_Refuses…` |
| J1 | `startPosition` off by one | 1 — `TheSweep_IsCappedPerRun_AndTheNextRunResumesAfterTheLastRecord` (asserts 1 then 2) |

**The r1 engine, seeded the same way:**

| Seed | Mutation | Failed |
|---|---|---|
| F1 | nothing re-decided after a move (the defect) | 16 (all 5 stamped shapes, the look-through, the put-back, the provisioning grandchild, …) |
| F2 | every move applied at once (no "out only once certain") | 2 (`NeverReleasesARowWhoseSupportIsMovingIn`, `…ThroughAUserOwnedParent` — the message is written) |
| F3 | a row pulled in and then ruled out is not put back | 1 |
| F4b / R22b | an ordinary row nobody moved loses its shares (round 22) | 3 / 1 |
| R22a | the mirror of a child isolated at the start is not removed | 1 |
| F5 | no put-back after a failed mirror removal | 1 |
| F6 | no mirror removal when the root is isolated (item 9) | 1 |
| F7 | only direct parents re-decided (no transitive closure) | 2 |
| S1r / S2r / S12r | re-checks of the original S1 (boundary filter) / S2 (SyncRootAsync) / S12 (read-back) on the rewritten loop | 3 / 6 / 2 |
| A3 / A4 | census: Restore writes a team of its own / is called with the current owner, not the snapshot | 1 / 1 (ArchTests) |
| A5 | census: `AssignAsync` writes the Secure team instead of the resolution | 1 (ArchTests) |
| F4 (a) | `LeftIsolation` without the `IsIsolated(start)` term | 0 — an **equivalent mutant**: a written row not isolated at the start can only have moved IN and been put back (current = start), which takes the no-change branch; F4b/R22b are the bites for round 22 |

**New owner-write path and census kind.** The put-back is an owner write whose value is NOT a resolution (it restores the
owner the pass read first), so it cannot sit in the Routed `AssignAsync`. It is its own member, `RestoreStartOwnerAsync`,
under a NEW census kind `Restore` in `RecordOwnerAssignmentCensusTests`, with its own assertion
(`EveryRestoreOwnerWriteWritesBackOnlyTheStartOwnerItsPassRecorded`): the value comes from the member's single `start…`
parameter (never a literal), and every same-file call passes the pass's snapshot (`start[key]` / `left.Previous`) —
seeds A3/A4. `AssignAsync` now takes the `RecordOwnerResolution` itself, so the Routed value check sees the resolution.
CLAUDE.md §11 for the new member [**corrected in r2, §13 item 4** — the clause on `RestoreOneAsync` below is inaccurate:
the primitive is table-generic in shape; the real reasons are given there]: (1) existing — `AssignAsync` writes only a
resolution (census), task 133's `RestoreOneAsync` restores only platform-cascade rows; (2) extension — folding it into `AssignAsync` would let the Routed
member write a non-resolution value, which the census exists to forbid; (3) cost of doing nothing — a released child whose
mirror removal fails keeps its former sharees permanently once the record's shares go (round 22 makes the next pass treat
it as never isolated), and a row pulled into isolation would be handed to the BU team instead of its own user. No new
service, registration, endpoint, option, job, column or package; the job's `ResultJson` gains one additive member
(`startPosition`).

**[Superseded by r2 — §13 item 1, owner round 24 item 1: the dry run now plans to the same fixpoint over planned owners.]**
**Observation (not a verifier item, not changed): the report-only plan is a lower bound for grandchildren.** A report-only
pass writes nothing, so it decides each row once against the owners it reads: a grandchild whose ONLY route into isolation
is a parent the same pass would move (e.g. a to-do filed only under an ordinary-team document of a secure record) is
planned only after that parent has moved — the dry run lists the parent, not the grandchild; the apply moves both (the
fixpoint), and `-Verify` then proves nothing is left. Making the dry run exact would need the resolver to accept planned
owners (a new input to the ONE rule) — recorded for the owner, not built.

**Complexity (§11.5).** `SecureChildReconciler.cs` ≈ 900 lines: one cohesive pass (walk → decide-to-fixpoint → assign /
restore → share → report); the fixpoint loop and its helpers share the pass's state. Not decomposed.

**Results (r1, 2026-10-04):** transitions suite 42/42 (19 → 42); affected suites 766/766; full BFF unit suite **14,828 =
14,774 passed + 54 skipped (pre-existing), 0 failed** (+23); ArchTests **373/373** (+1, the Restore assertion);
`Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **428 = 403 passed + 25 skipped, 0 failed**. CVE:
`dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` — no vulnerable packages. Publish size: not
measured (instruction). `dotnet format` on the changed C# files: no change.

**Handoffs added by r1:** task 150 F3 gate must cover the already-not-secure branch of `/unsecure-project` (item 10).

## 13. r2 fix round — verifier items 1-13 + owner round 24 (2026-10-04, branch `task/uac-r2-148-r2`, base `b2bcc5f3b`)

Owner decisions applied: rounds 1-13, 22 and **round 24** (2026-10-04, main session under the round-15 directive, read
from `work/unified-access-control-r2` `a178a2689`): **item 1** — exact dry run through a planned-owner overlay on the ONE
`IRecordOwnershipResolver` (dry-run plan == applied result, three-level tree tested); **item 2** — no rule-driven widening:
the sweep job and Write-gated provisioning never release an isolated child to its business unit; such rows are reported
`needs-f3` and stay isolated (round 6 "never auto-unsecure", round 10 item 7). Round 22 still binds.

| # | Verifier item | Disposition |
|---|---|---|
| 1 | MEDIUM — report-only plans only the first level (wouldChange 1, then changed 2) | **Fixed (round 24 item 1).** `RecordOwnershipContext.PlannedOwningTeams` (one property on the existing context — owner D-1, extend the one resolver in place): a parent named there is read as owned by its planned team (its business unit read from the team row; its existence and `sprk_issecure` still read from its own row; a planned team that cannot be found REFUSES, never falls back to the stored owner). `SecureChildReconciler` report-only now runs the SAME fixpoint rounds: each move is planned (`_planned`, a `plan:` log line per row) instead of written, the rows resting on it are decided again against the planned owner, a put-back drops the plan. Tests: `TheSweep_ReportOnly_PlansEveryLevelOfATreeReachedOnlyThroughRowsItWouldMove` (secure work assignment → ordinary document → analysis → to-do: plan 3 == apply 3, same ids, previous owners and teams — the verifier's probe shape plus one level); `TheReportOnlyPlan_IsExactlyWhatTheApplyChanges_ThroughAPutBack` (an IN, an OUT made only once certain, a row pulled in and put back, ITS child pulled in and put back, a refusal — plan set == apply set); `RecordOwnershipResolverTests` +2 (planned IN, planned OUT, stored owner without a plan; unfound planned team refuses). Guide §7c.1 step 1 and the script help now describe the plan as the apply's (assuming each write lands) |
| 2 | MEDIUM — the already-provisioned branch's incomplete-pass guard untested (seed K2 survived) | **Fixed.** `Provisioning_RepeatedOnAProvisionedRecord_WhoseChildPassIsIncomplete_IsNeverASuccess` × (nothing else written → would have been 409; another child moved → would have been 200 `childrenOnly`): 500 `children_incomplete` with counts, the refused child keeps its owner and gets no share, a third call completes it. **K2 re-seeded: 2 fail** |
| 3 | LOW — double fault (mirror revoke fails AND put-back fails) leaves no persisted trace | **NOT CLOSED — escalated (first-class stop; no owner decision covers it).** See "Item 3 escalation" below. No code changed for it |
| 4 | LOW — `RestoreStartOwnerAsync` vs 133's `RestoreAsync`; the §11 justification is wrong | **Justification corrected; the reading of round 10 item 4 is proven too broad from the owner's text.** Round 10 item 4 verbatim: "133, compensation's reverse Assign cascade: coordinate with task 148's child-ownership logic, then snapshot and restore each re-owned child's own owner (options (c) then (a))" — it binds the restore of the rows a ROOT's reverse Assign cascades to, and 148 DOES reuse 133's primitive for exactly those (`PlaceCascadeRowsAsync` → `AssignCascadeChildOwners.RestoreAsync`). The put-back restores a `sprk_*` child the pass itself moved, which no Assign cascade touches. The note header's paraphrase ("reuse … do not fork it") was broader than the decision and is corrected. The r1 §11 clause "RestoreOneAsync restores only platform-cascade rows" was false (the primitive is table-generic: `CascadeChild` carries LogicalName / EntitySet / IdColumn / Id / Owner) and is marked corrected. The real reasons, now in the member's remarks: (a) the scope of round 10 item 4 above; (b) the census classifies `RestoreOneAsync` as kind `Root` (rows a root's cascade owns, never a `sprk_*` child) — routing children through it would make that false, and it cannot take kind `Restore` instead, whose assertion requires the written value to be the pass's start-owner snapshot, because the reconciler also calls it with the resolver's owner for the cascade rows (round 13 item 1); (c) every other read and write of these rows in the pass is on `IGenericEntityService` (the resolver's client) and the put-back shares the pass's `ReadBackAsync` with `AssignAsync` — a put-back on the Web API client would be a second path for the same rows |
| 5 | LOW — "prints every planned change" vs a 200-change sample with no warning; step 2's "every change moves INTO" vs OUT moves | **Fixed.** The run's `ResultJson` counts every change (`changesTotal`) beside the listed ones (`changesListed`); `Invoke-SecureChildBackfill.ps1` refuses a report without the counts (a BFF older than the script) and WARNS per run and in the summary when a list is shorter than the count, naming the complete list (the BFF's per-row `plan:` / `reassign:` lines — report-only now logs a `plan:` line for every planned row, cascade rows included). Step 2's sentence is TRUE again under round 24 item 2: the sweep never moves a row out; a row the rule would release is listed with outcome `NeedsF3`, counted (`needsF3`), and warned about. Guide §7c.1 steps 1, 2 and 4 rewritten to match. Tests: `TheSweepReport_CountsEveryChange_EvenBeyondTheListedOnes` (201 + 1 changes: total 202, listed 200, needsF3 still counted); offline: `scratchpad/t148r2/verify-pass.ps1` 9/9 over the script's own `Invoke-Pass` (the 6 r1 cases + truncated-list warning, needs-f3 counted, pre-r2 BFF refused) — script seeds S1 (no truncation warning), S2 (needsF3 not summed), S3 (counts not required) each turn one case red |
| 6-9 | VERIFIED OK | No change |
| 10 | AC7 not met | **Met** — item 1 (and round 24 item 2: nothing the sweep plans moves a row out) |
| 11 | AC3 partially not met | **Met** — item 2 |
| 12 | AC9 live gate | **PENDING** — G148-1..4 (§8), main session; G148-3 now also records `needsF3` and `changesTotal` vs `changesListed` |
| 13 | AC11 publish size not reported by the executor | Verifier's measurement at `b2bcc5f3b`: base `e94a8bc2d` **45.85 MB** (48,080,695 B, 212 files) → **45.89 MB** (48,119,081 B, 212 files), **+0.04 MB**, PowerShell `Compress-Archive` Optimal, PDBs included, fresh short-path worktrees. r2 adds no package and no file; **not re-measured** (instruction). Carry the verifier's number into the PR. CVE scan below |

**Round 24 item 2 — no rule-driven widening (implemented).** `SecureChildReconciler.ReconcileAsync` takes a
`SecureChildPassTrigger` (replacing the `bool unsecuring`): `Provisioning`, `Sweep`, `Unsecure` (mid-transition — the
`UnsecuringRoot` exemption, as before) and `UnsecureCompletion` (the already-not-secure branch — releases, no exemption, its
cascade rows move only across the boundary, as before). Only the two unsecure triggers may release. In a `Provisioning` or
`Sweep` pass a row that was isolated when the pass BEGAN (round 22's snapshot) and that the rule would hand an ordinary
team is not moved: outcome `NeedsF3` (new `SecureChildRowOutcome` member), never written, its shares untouched, listed with
its owner and the team an unsecure would give it, counted per table (`SecureChildTableCounts.NeedsF3`), in the endpoint
responses (`children.needsF3`, `children.tables[].needsF3` — additive) and in the run's `ResultJson` (`needsF3`). A row the
pass itself pulled in and whose support then leaves is still put back on its own start owner (undoing the pass's own move
is not a release of an isolated child). A held row does not make the pass `Incomplete`: no call of the same trigger could
ever move it, so `Incomplete` would make provisioning answer 500 forever and the sweep fail every run; it is reported
instead (the "needs-f3" state round 24 names), and `-Verify` passes over it while listing it. The platform-cascade rows
need no hold: a non-releasing pass only runs over an isolated root (the rule puts them ON the Secure team) or a
flagged-not-isolated one (the rule refuses) — a hold written there could not be reached through the real resolver (seeded:
N6 survived, so it was removed rather than shipped unprovable). Tests: `TheSweep_NeverReleasesAnIsolatedRow_ItReportsItNeedsF3_AndKeepsItsShares`
(replaces r1's `TheSweep_RemovesTheMirrorOfARowItTakesOutOfIsolation`: writes on, the to-do stays on the Secure team with its
share, needsF3 1, changed 0, mirrorsRevoked 0, run successful; the dry run reports the same), `Provisioning_NeverReleasesAnIsolatedRow_ItReportsItNeedsF3`
(200 Completed, the record's own document still goes in, the held to-do keeps its owner and its share, `needsF3` 1 overall
and on `sprk_todo`), and the counts test above. The unsecure tests (release allowed) are unchanged and green.

**Item 3 escalation (first-class stop — no owner decision answers it).**

> 🔔 **Human Input Required — task 148 r2, verifier item 3 (LOW): a double-fault stranded mirror leaves no persisted trace**
>
> - **Situation.** On unsecure, a child released from isolation whose mirror revoke fails is put back on the Secure team
>   (r1). If that put-back ALSO fails, the child stays on its business unit's team carrying some of the record's former
>   sharees' shares; this is logged Critical once and the call answers 500 `children_incomplete` (flag and record shares
>   kept — no over-share yet: the sharees still hold the record). A SECOND `/unsecure-project` call snapshots that child as
>   never isolated (round 22), keeps its shares, completes, revokes the record's shares and clears the flag. From then on
>   nothing reports the stranded mirror, and the former sharees keep a child of a record they no longer hold.
> - **Why it is not fixable in code alone.** Any fix needs state that survives between the two calls, and the only state
>   is Dataverse's: the child's owner (the failed write) and its shares (indistinguishable, by round 22, from a user's own
>   share on a never-isolated child). A trace written AFTER the double fault is written when writes are failing; a trace
>   that survives must be written AHEAD of the release — which changes round 22's definition of "was isolated", a binding
>   owner decision.
> - **Options.**
>   (A) **Write-ahead release marker (recommended).** Before re-owning a child OUT, grant a Read share on it to a
>   memberless principal — the Secure Record BU's DEFAULT team (memberless by the same invariant provisioning enforces: the
>   BU holds no users) — and have `RemoveMirrorAsync` revoke it LAST, only after every other share is confirmed gone. A pass
>   treats an ordinary child that still carries the marker as "was isolated" (round 22 extended: owned by the Secure team
>   when the pass began, OR carrying the release marker). The second call then removes the stranded mirror and does not
>   complete until it can. Costs: one extra grant + revoke per released child; a live probe that the BFF identity may share
>   to that team and that the team stays memberless in every environment (a pending manual gate); round 22 amended.
>   (B) **A schema column** on every child table (e.g. `sprk_isolationreleasepending`, set before the release, cleared
>   after the mirror is gone): explicit, but a schema step on ~20 tables with dry run / apply / verify and FLS.
>   (C) **Accept the residual** (LOW: a triple-fault-class event — revoke failure + owner-write failure on the same row —
>   bounded to the record's own former sharees, logged Critical with the row id and the manual revoke). Document it in the
>   guide's §8 "must NOT" table and the residual-risk section.
> - **Recommendation.** (A) — it closes the hole without schema, keeps round 22's intent (a share the secure team's mirror
>   wrote is the only kind removed) and fails closed (no marker can be written → the child is not released).
> - **Impact if (A) or (B) is chosen.** Amends round 22's snapshot rule; adds one owner-approved live gate; touches
>   `SecureChildShareSynchronizer.RemoveMirrorAsync` and the reconciler's release path only.

**New surface (CLAUDE.md §10 / §11).** No new service, DI registration, endpoint, option, job, column or package; no
plugin (ADR-002). Placement unchanged (BFF; ADR-052 "BFF, schedule").

| New surface | Existing (grep) | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordOwnershipContext.PlannedOwningTeams` (one property on the existing context) + a branch in the resolver's `ReadParentAsync` | The resolver's `ReadParentAsync` reads a parent's STORED owner; nothing lets a caller ask "which owner if this parent were moved" (grep `PlannedOwning`: none before r2) | Extends the ONE resolver in place (owner D-1; round 24 item 1 names it), exactly as `UnsecuringRoot` did; the rule is unchanged — only a planned row's facts are the planned ones | the dry run under-reports grandchildren (verifier item 1); the owner's pre-apply review — including trigger 1, documents in shared SPE containers — is a lower bound |
| `SecureChildPassTrigger` (enum replacing `ReconcileAsync`'s `bool unsecuring`) | `bool unsecuring` carried one fact (mid-transition) and could not carry the second (may this pass release?) — the already-not-secure branch releases but is not mid-transition | Replaces the parameter (5 call sites: 2 provisioning, 2 unsecure, 1 job); two bools would allow the meaningless "mid-unsecure but may not release" | round 24 item 2 could not be told apart from the unsecure completion branch, which must still release (owner round 11 item 3) |
| `SecureChildRowOutcome.NeedsF3`, `SecureChildTableCounts.NeedsF3`, `SecureChildPassSummary.NeedsF3` / `SecureChildPassTable.NeedsF3`, `ResultJson.needsF3` (additive JSON) | the outcomes had no "held for another actor" state; `Refused` means the rule could not decide | extend the existing enum / records / DTOs (one member each) | a held row would be reported as untouched (invisible) or refused (wrong cause, and Incomplete forever) — round 24 requires it reported as needs-f3 |
| `ResultJson.changesTotal` / `changesListed` (additive) | `changes` was capped at 200 with no count | one counter in the existing loop | a truncated list reads as the whole plan (verifier item 5) |

**Seed-and-bite (r2)** — each seeded in production code by `scratchpad/t148r2/seed.ps1` (one regex replacement, build,
the transitions + resolver suites, file restored from its bytes and touched; `git diff --stat` unchanged after every
batch):

| Seed | Mutation | Failed |
|---|---|---|
| K2 | the already-provisioned branch ignores an incomplete pass (condition never true) | 2 — the new item-2 theory |
| P1 | the resolver ignores `PlannedOwningTeams` | 4 — both resolver tests, the 3-level and put-back plan tests |
| P2 | an unfound planned team falls back to the stored owner | 1 — `…WhenAPlannedTeamCannotBeFound_Refuses…` |
| P3 | report-only stops after round 1 (the r1 behaviour) | 6 |
| P4 | the reconciler never passes its plan to the resolver | 2 — the 3-level and put-back plan tests |
| P5 | a report-only put-back keeps its plan | 1 — the put-back plan test |
| P6 | `changesTotal` counts only the listed changes | 1 — the counts test |
| P8 | a planned change reported `Changed` | 6 |
| P9 | report-only assigns for real | 6 |
| P10 | report-only puts back for real | 1 — the put-back plan test |
| N1 | no hold: a non-releasing pass releases | 3 — sweep needs-f3, provisioning needs-f3, counts |
| N2 | every trigger may release | 3 (same) |
| N3 | no trigger may release | 16 — every unsecure release test |
| N4 | the unsecure completion branch may not release | 2 — both already-unsecured-record tests |
| N5 | the endpoint passes `Sweep` on the completion branch | 2 (same) |
| N6 | the cascade-row hold disabled | **0 — unreachable through the real resolver; the hold was removed** (reason above) |
| (P7 — direction field) | — | moot: the direction field was dropped once round 24 item 2 made every sweep move an IN |

**Results (r2, 2026-10-04):** transition + resolver suites **107/107** (transitions 42 → 48, resolver +2); affected
suites **774/774**; full BFF unit suite **14,836 = 14,782 passed + 54 skipped (pre-existing), 0 failed** (+8 vs 14,828);
ArchTests **373/373**; `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **428 = 403 passed + 25
skipped, 0 failed**; offline script harness 9/9. CVE: `dotnet list package --vulnerable --include-transitive` on
`Sprk.Bff.Api` — no vulnerable packages. `dotnet format whitespace --verify-no-changes` on the changed BFF files: clean.
Every seed above was re-run on the final code (after the source was restored the test project was rebuilt before the
final runs). No client change: the response fields are additive.

**`.claude/**` edits needed:** none.

**Handoffs added by r2:** item 3 escalation (above) — the owner chooses A / B / C. Task 150's F3 gate (r1 handoff) now
covers BOTH releasing triggers: the transition (`SecureChildPassTrigger.Unsecure`) and the already-not-secure branch
(`UnsecureCompletion`). Task 147 (scheduling the sweep): the sweep releases nothing; `needsF3` rows are a standing report
for an F3 holder, not a failure.
