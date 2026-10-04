# Task 149 — people shared on a secure record see exactly its children (C10 part 2, sharees; #1071)

> Branch `task/uac-r2-149` (base `task/uac-r2-146-b2-r2` + `work/unified-access-control-r2` merged at `874bb1c2f`);
> fix round r1 on `task/uac-r2-149-r1` (§11 — the adversarial verifier's findings, item by item);
> fix round r2 on `task/uac-r2-149-r2` (§12 — the second verifier's findings: five fail-closed guards pinned, the
> unsecure gap made a ship gate, the 143 coordination made an orchestrator obligation);
> fix round r3 on `task/uac-r2-149-r3` (§13 — merged with task 143 and DISCHARGED the merge-order obligation: the No
> Access guard before child grants/widenings, the enforcer fans out, the walled-user negative test; verifier findings
> 1-3; owner round 11 recorded; the 133/146 creator-stamp conflict resolved per owner round 10);
> fix round r4 on `task/uac-r2-149-r4` (§14 — the multi-root half of the No Access guard pinned by test; the task-142
> wiring made a BINDING merge-order obligation; the Secure-flag edge of the wall recorded).
> Status: **code complete, not deployed.** Ships together with task 146. Live steps are manual gates (§8).
> Owner decisions that bind this task: round 7 item 5 ("each child's principals and rights equal the root's POA share
> set ... never wider"), round 5 (production topology), round 6 (secured child roots stay secure; task 158), round 3
> R3/R4 ("minutes, never hourly"), C4 (Write-holders share out of the box), **round 11 items 2-4 (2026-10-03): the
> ≤2-minute window accepted with the job writing and no platform cascade; the ship gate "no record unsecured in a
> shared environment until 148 deploys"; INTERSECTION for two secure roots; ShareAccess not mirrored.**

## 1. Outcome

Once task 146 owns every child of a secure project, matter or work assignment by the memberless `Secure Record Owners`
team, the only way to reach a child is a share on the child row. **Dataverse provides none** (§2), so the BFF now keeps
every Secure-team-owned child shared with exactly the internal principals its secure root is shared with:

| Rule | Implementation |
|---|---|
| Who | every SYSTEM USER and TEAM with a direct POA share on the root (inherited-only rows, mask 0, are not shares) |
| Rights | the root's rights restricted to Read, Write, Append, AppendTo, Delete — **never Share** (owner round 11 item 4), never Assign, never Create (`RecordShareLevels.ChildMirrorMask`) |
| Several secure roots | the **intersection**: a principal must be shared on every one, at the lowest rights (owner round 11 item 4) |
| No Access list (task 143, r3) | a system user the guard refuses (walled, or unverifiable) for ANY of the child's secure roots is never granted or widened on the child; they keep only the narrowing part of a change. Every root is asked, each answer remembered per (root, user) — pinned in r4 by a two-root test (§14) |
| Anything else on the child | revoked; a wider share is narrowed; a missing one granted |
| Which rows | rows of the 23 codified child tables owned by the Secure team, whose lookups lead (through Secure-team-owned or user-owned children, never ordinary-team-owned ones) to a Secure-team-owned root |
| Never touched | children of non-secure roots; rows owned by any other team; the roots themselves |

One component, `Services/Access/SecureChildShareSynchronizer.cs`, over the one POA seam. Four triggers:

1. **`/share-user` and `/unshare-user`** fan out in the request, after the root write is confirmed (also when the root
   was already right / already absent, so repeating a request completes it). Some children not updated → **500
   `sdap.access.user_share.children_incomplete`** with `childrenInScope`, `childrenUpdated`, `childrenNotUpdated`,
   `childrenHeld` and the root outcome; the root write stands (an unshare is never rolled back).
2. **Secure provisioning** (Step 8, on EVERY successful path: after a new container is recorded, and on the
   kept-container path of task 133 b2 — §15) — best effort, logged; normally a no-op today (task 148 re-owns existing
   children, then calls the same synchronizer).
3. **`SecureChildShareReconciliationJob`** — every two minutes, every secure child in the environment. This is the
   mechanism for new and re-filed children (about thirty BFF writer sites plus the client writers of task 147) and for
   out-of-the-box MDA Share/Unshare of a secure root. Owner round 11 item 2 accepted it as the mechanism (≤ 2 minutes,
   writes on, no platform cascade).
4. **`NoAccessShareEnforcer`** (r3, task 143 merged) — after it removes a walled user's share on a secure root, it
   calls `SyncRootAsync(root)`, so the user leaves every child in the same call; a fan-out that cannot finish is a
   `children-incomplete` failure in its report (the reconcile completes it).

## 2. Part A — live evidence (read-only, spaarkedev1, 2026-10-02)

### 2a. Cascade configuration of every root→child relationship

`GET EntityDefinitions(LogicalName='<root>')/OneToManyRelationships?$select=SchemaName,ReferencingEntity,ReferencingAttribute,CascadeConfiguration`
for `sprk_project` (35 relationships), `sprk_matter` and `sprk_workassignment`, plus
`ManyToOneRelationships` of all 23 child tables (raw output kept in the session scratchpad):

| Relationships | Assign | Share | Unshare | Reparent | Delete |
|---|---|---|---|---|---|
| **Every Spaarke root→child relationship** (agreement, analysis, billingevent, budget, budgetrevision, communication, communicationrule, communicationthread, decisionrecord, document ×2 per root, event, externalrecordaccess, invoice, kpiassessment, memo, policy, reportcard, servicerequest, signal, spendsignal, spendsnapshot, todo, workassignment) | NoCascade | **NoCascade** | **NoCascade** | **NoCascade** | RemoveLink |
| Every child→child relationship among the 23 codified tables | NoCascade | NoCascade | NoCascade | NoCascade | — |
| System: `team`, `sharepointdocument`, `sharepointdocumentlocation` | Cascade | Cascade | Cascade | Cascade | Cascade |

The system Assign cascade is the one owner round 4 item 3 already accepted. **No relationship would re-own children on
an Assign** (escalation trigger 1 does not fire). **The platform shares nothing to children, for any case** — so no case
can be assigned to the platform without a schema change, and that change is table-wide (trigger 5, §5).

### 2b. Other live facts used

- Entity sets of the 26 tables (`EntityDefinitions(...)?$select=EntitySetName`): logical name + `s`, except
  `sprk_analysis` → `sprk_analysises`. Primary id = logical name + `id` everywhere. All UserOwned.
- `Secure Record Owners` team = `6eabc7f9-13be-f111-a05b-0022482913fc`; it owns **1** project (`65a3fab2…`, the task 144
  fixture) and **0** matters, work assignments, documents, to-dos, events, communications or memos (146 not deployed).
- The batched POA read shape answers 200 and returns the live row shape:
  `principalobjectaccessset?$filter=objecttypecode eq 'sprk_project' and (objectid eq A or objectid eq B)&$select=objectid,principalid,principaltypecode,accessrightsmask,inheritedaccessrightsmask,changedon`
  (one direct systemuser share, mask 262167, inherited 0).

### 2c. The probe matrix (step 2) — NOT RUN: live writes are outside this session's authority (G149-1)

| Case | Platform today | Mechanism chosen | Evidence |
|---|---|---|---|
| (a) cascade configuration | NoCascade everywhere | — | §2a (read) |
| (b) child created after the root was shared | no cascade → nothing | synchronizer (reconcile, ≤ 2 min) | §2a + tests; live G149-2 |
| existing child, root shared to B (BFF) | nothing | synchronizer (in the request) | tests; live G149-2 |
| (c) reparent into a secure root | nothing | synchronizer (reconcile, ≤ 2 min) | tests; live G149-2 |
| (d) modify / unshare (BFF) | nothing | synchronizer (in the request) | tests; live G149-2 |
| MDA share / unshare of a secure root | nothing | synchronizer (reconcile, ≤ 2 min) | tests; live G149-2 |
| `sprk_relatedproject`-only document | nothing | synchronizer (every lookup is a lineage edge) | test `Reconcile_AChildCreatedAfterTheShare…` (DocRelatedOnly) |
| matter, work assignment | nothing | synchronizer | test `Reconcile_CoversMattersAndWorkAssignments` |
| (e) parental-relationship limit on `sprk_document`'s six root lookups | not needed: no cascade is proposed | — | — |
| (f) cascade would also cascade Assign | no Spaarke relationship cascades Assign | — | §2a |
| **(g) a child's POA share survives an Assign to the Secure team** | **unknown — needs a write** | consumed by task 148's ordering | **G149-1 step 4** |
| (h) a cascade is table-wide | true by construction | not used (§5) | — |

Because the platform covers no case today, the mechanism decision does not depend on the probes. Only (g) — which task
148 consumes — needs a live write, and (b)/(d) need one only if the owner chooses the cascade option (§5, decision 3).

## 3. Mechanism decision

**The BFF synchronizer covers every case; the platform cascade covers none** (no mix, so trigger 3 does not fire).
Why not inline at each create site: the ownership resolver decides an owner BEFORE a row exists, so a mirror needs the
row id at about thirty writer sites across 35 files (task 146's census) — editing all of them, plus every future writer
and the client writers of task 147, is the shape of gap that let this defect exist. The reconcile covers every writer,
present and future, from one place. Its cost is the latency in §5 decision 3.

## 4. What the synchronizer does, precisely

0. Scoped run (`SyncRootAsync`, r1): read the root's own row first. Not team-owned, or owned by a team whose own row
   proves it is not the Secure team (another name, a default team, not an Owner team) → `NotApplicable` without
   consulting the Secure Record BU (F7). A fault reading the root → `Failed`.
1. Resolve the Secure Record owner team (BU by name TOP 2, then the named non-default Owner team TOP 2 — the
   resolver's rule). No BU or no team → `NotApplicable` (no secure records exist). Two of either → `Failed`.
2. Scoped run (`SyncRootAsync`): the root must be owned by the Secure team, else `NotApplicable` (nothing read below).
3. Load the candidates. **Reconcile:** every Secure-team-owned row of the 23 child tables (one paged query per table,
   the lineage lookups selected). **Scoped run (r1, F4/F9):** only the root's DESCENDANTS — the lineage lookups walked
   downward level by level from the root (one query per child table per level, its lookups into the previous level
   OR-ed, chunked at 200 ids), through Secure-team-owned and user-owned rows only (never an ordinary team's), at most 6
   levels — exactly the rows whose upward walk can reach the root. Rows elsewhere in the environment are never read.
4. Each row's secure roots: walk its lookups upward (cycle-safe, ≤ 6 levels). A Secure-team-owned root counts; an
   ordinary root contributes nothing; a missing parent, a root flagged `sprk_issecure` but not isolated, or a chain too
   deep makes the row **held**. A child owned by a user is looked through; one owned by an ordinary team is not. The
   upward walk stays the authority in a scoped run too (a candidate under a second secure root gets the intersection).
   A fault reading a row's filing → nothing written on it; it counts as examined AND not updated (r1, F2/F4).
5. Each root's mirror from the STRICT share read; a root that cannot be read → no write on any child that needs it.
6. Each child's shares from the new batched strict read (`GetPrincipalAccessForRecordsOrThrowAsync`, 25 per call); a
   batch that cannot be read → no write on those children.
7. Diff and write: revokes, then narrowings (ModifyAccess), then grants (GrantAccess only where no direct share exists —
   task 063's rule). A held child is only revoked/narrowed, never granted or widened (pinned since r3 by
   `AHeldChild_WhoseShareIsNarrowerThanTheKnownRootsAllow_IsNeverWidened`). Anything that ADDS a right is re-checked
   against the roots read FRESH right before it is written, so a root unshare that lands mid-run (the endpoint's
   fan-out racing the reconcile) is not undone by a stale grant; if that fresh read fails, nothing is added, and a
   mixed change keeps only its narrowing part (r3). Then (r3, task 143) every system user still to be granted or
   widened is checked against the No Access list for each of the child's secure roots: refused (walled, or the check
   unanswerable) → nothing added, only the narrowing part kept; unanswerable also leaves the child not updated. A child
   left with nothing to write counts as unchanged. Every changed child is read back. (r4: the guard answers by the
   root's `sprk_issecure` FLAG, while this step 4 counts a root as secure by its Secure-team OWNER; a Secure-team-owned
   root whose flag reads No/empty is therefore not walled for its children — §14 item 7.)

## 5. Escalations (CLAUDE.md §6 / §6.5). None blocks the code; each is recorded with the default implemented.

| # | Trigger | State |
|---|---|---|
| 1 | A relationship cascades Assign | **Does not fire** (§2a). |
| 2 | A child under TWO secure roots with different sharee sets | **DECIDED — INTERSECTION** (owner round 11 item 4, 2026-10-03: "a child under TWO secure roots gets the INTERSECTION of their sharee sets (fail closed)"). Implemented since the first round; nothing changes. |
| 3 | Mixed cascade + synchronizer | **Does not fire** (synchronizer only). |
| 4 | MDA Share/Unshare cannot be synchronized inside an accepted window | **DECIDED — window accepted** (owner round 11 item 2, 2026-10-03: "a window of at most 2 minutes is accepted for MDA Share/Unshare and for NEW or RE-FILED children ... The scheduled reconcile is the mechanism, and it ships with writes on. Dataverse's table-wide Share/Unshare/Reparent cascade is NOT enabled"). The deployment-gating constraint's condition is met: the reconcile runs on a schedule AND the owner accepted the window in writing. Removing prvShare from ordinary roles was never proposed (it contradicts C4). The earlier recommendation (a targeted inline mirror at the interactive create paths if UX testing shows the create latency matters) stays a future option, not work for this task. |
| 5 | Enabling Share/Unshare/Reparent cascade is table-wide | **DECIDED — not enabled** (owner round 11 item 2). The guide keeps it as a "must NOT". Probe (b) of G149-1 (whether a Reparent cascade gives inherited access on create) is therefore no longer needed. |
| 6 | ShareAccess mirrored onto children | **DECIDED — not mirrored** (owner round 11 item 4: "ShareAccess is NOT mirrored onto children"). Sharing happens at the root and fans out. Implemented since the first round; nothing changes. |
| — | Job posture | **DECIDED — writes on** (owner round 11 item 2: "it ships with writes on"). Unlike the report-only jobs of tasks 137/141, this one IS the mechanism, and every write is bounded by the root's own shares. |

**Other root-share writers.** Task 143 (No Access for internal users on secure records) merged into THIS branch in r3,
and **this task wired it** — the merge-order obligation binds whichever of 143/149 lands second, and 149 lands second
(§12 "Merge-order obligation", discharged in §13): the synchronizer asks `SecureShareNoAccessGuard` about each child's
ROOT before any child grant or widening, `NoAccessShareEnforcer` calls `SyncRootAsync(root)` after it removes a root
share, and the walled-user negative tests exist. (Before r3 this paragraph said "142 and 143 do" and that deriving from
the root satisfied the guard; both statements are superseded: the guard is now consulted, because a walled user's ROOT
share can stand — the enforcer has not run, or owner S5 kept it — while the mirror would otherwise grant the children.)
Task 142 (Assigned-To POA shares on roots) has not landed on this branch: until it does, its root-share writes reach the
children through the 2-minute reconcile, and the guard above applies to them too. **r4: wiring 142 is a BINDING
merge-order obligation, not an option** — whichever of 142 and 149 merges second makes 142's materializer call
`SyncRootAsync(root)` after a confirmed root share write and adds the tests (§14, "Merge-order obligation with task
142"). (r3 said "142 calls `SyncRootAsync` after its own write if it wants them at once"; superseded.)

**Handoffs.** Task 148: a child moved OUT of a secure record (or a record made ordinary — `UnsecureProjectEndpoint` now
says why it does not fan out) keeps its mirrored shares until 148 re-owns it; a child moved between two secure records
keeps the old root's sharees for ≤ 2 minutes. 148 needs probe (g). Task 158: a secure work assignment/project under a
secure matter is a ROOT here — whether the parent's sharees are shared on that root is 158's design; the synchronizer then
mirrors whatever the child root carries. Task 147: client creates are mirrored by the same reconcile; no second
mechanism.

## 6. Placement (CLAUDE.md §10) and justification (§11)

**Placement:** all in the BFF. bff-extensions.md decision criteria: the fan-out writes in the same request lifecycle as
the root share (the caller is told what happened) → BFF; the reconcile is low volume, BFF identity and BFF domain code
on the in-process scheduler (ADR-052 "BFF, schedule"; ADR-036). No Functions/Container Apps signal applies.

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `SecureChildShareSynchronizer` (service + 1 DI line, concrete singleton) | `IDataverseRecordShareService` writes POA (grep `GrantAccessAsync(`): InternalShareEndpoints, ProvisionProjectEndpoint and UnsecureProjectEndpoint share ROOTS; PlaybookSharingService shares playbooks; **DirectThreadAccessService writes on two CHILD tables** (corrected in r1 — the original row said "roots only", which was false, F3): Read on a Direct thread it creates user-owned (never a secure child, untouched by the synchronizer) and Read on each message for its thread's participants — which r1 makes skip a Secure-team-owned message. Nothing mirrors to children | Not in the seam (a pass-through testing seam, ADR-010); not in InternalShareEndpoints (provisioning + the job need it); not in the resolver (decides before the row exists) | after 146, every internal sharee of a secure record — its creator included — loses every document, event, to-do and communication on it; MDA unshares never reach the children |
| `SecureChildShareReconciliationJob` (`IScheduledJob` + `AddScheduledJob`) | `ExternalAccessReconciliationJob` (contact grants), `SecureRecordIsolationCensusJob` (read-only census), `MembershipReconciliationJob` (junction) — none touches child POA | Folding into the census would give a read-only job a write path | new/re-filed children and MDA Share/Unshare never reach the children; the deployment gate cannot be met |
| `SecureChildLineage` (static map, not a service) | `RecordOwnershipResolver.OwnershipParentEntities` lists tables, not columns, and walks every column of a row by reading `ColumnSet(true)` | Reading every column of every secure row each run is the cost the map avoids; pinned to the codified role set and the resolver's child set by test | — (a map, not a component) |
| `GetPrincipalAccessForRecordsOrThrowAsync` (method on the existing seam + `DataverseWebApiService`) | the single strict read | extends the ONE POA client (`PoaShareClientSingletonGuardTests` stays green) | one GET per child per run spends the application user's request budget every two minutes |
| Reason code `children_incomplete` + extensions | the `/revoke` 500 + ProblemDetails precedent for a partly-applied write | — | a partial fan-out would be a silent 200 or a bare 500 |

No endpoint, package, option, column or relationship change. Client: `AccessGrantModal` shows the server's sentence for
the new reason code (share and unshare) instead of "1 failed" / "Failed to revoke".

## 7. Tests

New: `tests/integration/data-mutation/ExternalAccess/SecureChildShareMirrorTests.cs` (**40**) over
`SecureChildShareWorld.cs` (an in-memory Dataverse that evaluates each QueryExpression, with ordinary decoys) and
`FakeRecordShareTable` (extended: child entity sets, batched read, per-record read/write faults, write log);
`tests/unit/domain/Access/SecureChildLineageTests.cs` (**10** incl. 6 theory cases); `DataverseRecordShareWireTests` +5
(batched read); `SecureProjectShareTests` +1 (provisioning fans out); `AccessGrantModal.userShare.test.tsx` +2. Existing
`InternalUserShareTests`, `ExternalAccessContractFixture`, `ProvisionProjectTestFixture` pass the synchronizer over a
world with no secure records (their contracts are unchanged).

Covered: mirror at create (reconcile) without Share; Full → Delete; matters and work assignments; a grandchild through a
user-owned communication; NOT through an ordinary-team-owned one; extra child share revoked; wider narrowed; Share
stripped; MDA share reaches every child and MDA unshare removes it; idempotent; team principals mirrored, the Secure team
never; NEGATIVES — a non-sharee never gains a child, R's sharee never gains R2's children, an ordinary root's children
receive and lose nothing, Share/Assign never added, an ordinary-team-owned child of R never mirrored; intersection for two
roots; root read fails → nothing written; child batch read fails → nothing on that table; held child only narrowed; a
read fault → Failed, nothing written; no Secure BU → nothing read; scoped run touches only that root; a write Dataverse
did not keep is not "updated"; endpoints: share fans out, level change changes every child, unshare removes from every
child, repeat-unshare completes an earlier one, partial fan-out → 500 with counts + root stands + reconcile completes it,
unshare partial → 500 + root not rolled back, children unreadable → 500 not bare, root unreadable → read_failed and no
write, caller cannot grant → nothing fanned out, narrowed share mirrors the narrowed mask, last-reader refusal fans out
nothing, held child named on share and still revoked on unshare; job success / partial (no throw) / Failed (throws).

**Seed-and-bite** (each failed its tests, then passed once restored, file touched): P1 Share mirrorable (10 failures);
P2 owner predicate dropped (7); P3 union not intersection (1); P4 endpoint fan-out removed (9); P5 unreadable root read as
"no shares" (1); P6 held child granted (1); P7 incomplete answered 200 (3); P8 user-owned parent not looked through (1);
P9 ordinary-team parent followed (1); P10 Secure team mirrored (1); P11b revokes never written (6) — P11 (skip the
"not desired" branch) did NOT bite because the narrowing branch revokes a mask with no Read anyway: equivalent, not a
hole; P12 provisioning fan-out removed (1); P14 job swallows Failed (1); P15 read-back skipped (1, after adding the test
that found it unguarded); P16 lineage drops `sprk_relatedproject` (13) and, after the document entry was re-derived from `DocumentLinkFields`, P16b the project links excluded from it (12); P17 a table renamed out of the lineage (2);
P18 batched read accepts a row for an unasked record (1); P19 unshare treats held as incomplete (1); P20 the fresh
re-check before a grant skipped (1 — `ARootUnshareThatLandsMidRun_IsNotUndoneByAStaleGrant`, added with the fix the
code review asked for); client C1 code not
in the refusal set (1), C2 unshare branch never taken (1).

Test scope note: tests stop at the mirror/fan-out/reconcile contract and its negatives (AC 14); the scheduler wiring is
covered by the ADR-036 ArchTest (`ScheduledJobsRegisterThroughAddScheduledJobOnly`).

## 8. Manual gates (live writes — the main session runs approved ones)

**Order: G146-1 (role 9 → 26) → deploy 146 + 149 together → G149-2.** G149-1 may run before or after the deploy.

**G149-1 — Part A probes (pre-deploy is fine; owner-approved test users only; delete probes).** With the Web API as
an administrator, `MSCRMCallerID: <user>` for reads as a test user:

1. Create probe `sprk_project` R (`sprk_issecure=true`), `PATCH ownerid@odata.bind → /teams(6eabc7f9-13be-f111-a05b-0022482913fc)`.
2. `POST GrantAccess` R → user A (Read). Create `sprk_document` C1 with `sprk_Project@odata.bind → R` owned by the team.
   `GET sprk_documents(C1)` as A → **expected DENIED** (no cascade). Record.
3. Create C2 after sharing R to B → B reads C2? **Expected DENIED.** Unshare B → nothing changes. Record.
4. **(g)** Create user-owned `sprk_document` C3 (owned by A), `POST GrantAccess` C3 → B (Read), then
   `PATCH C3 ownerid@odata.bind → /teams(<secure team>)`, then `GET principalobjectaccessset?$filter=objectid eq C3`
   and `GET sprk_documents(C3)` as B → **does B's share survive the Assign?** (also check
   `organization.sharetopreviousowneronassign` = false, guide §7 5b). Task 148 consumes this answer.
5. Repeat step 2 for a matter, a work assignment, and a document linked only via `sprk_relatedproject`.
6. Delete every probe; record ids and the deletion.

**G149-2 — after deploy with 146 (the task's step 8 / AC 12).** With existing non-admin test users (owner-created per
round 4 item 1; no relocation):

1. Provision a secure project; `/share-user` A at Collaborate → A reads every child created through each writer family
   (document upload, event, to-do, Office save) within ≤ 2 minutes, at mask 23 on the child (no Share).
2. `/share-user` B (View) → B reads existing children immediately (response 200); change B to Full → child mask 65559.
3. `/unshare-user` B → B denied on every child immediately.
4. MDA: share the project with D in the model-driven Share dialog → D reads children within ≤ 2 minutes; unshare D in
   the dialog → D denied within ≤ 2 minutes.
5. A user not shared on the project is denied every child throughout (pairs with G146-4).
6. `/api/admin/jobs/secure-child-share-reconciliation/status` → last run Success, heartbeat `status=Completed`.
7. Delete probes; record.

## 9. Deviations

1. **Create / re-file mirror is the two-minute reconcile, not an inline call at each writer** (§3, §5 decision 3).
2. **No fan-out on unsecure** (`UnsecureProjectEndpoint` comment): from that point the record is not secure, and
   revoking the children's shares before task 148 re-owns them would leave them readable by nobody. **r2: this departs
   from owner round 7 item 5 ("kept in sync on ... secure/unsecure") and step 5, so it is now a SHIP GATE and owner
   decision 4 (§12 item 7), not only a deviation.**
3. **The reconcile job is built here**, not in 148 (the deployment-gate constraint allows "an interim schedule of the
   same job"); 147/148 call the synchronizer, they do not add a second job.
4. **Shared-lib client change** (`AccessGrantModal`) needs the hosts that bundle it (TrackingFieldTrio PCF, SpaarkeAi)
   rebuilt at deploy for the improved message; without the rebuild the server's 500 shows as a generic failure (the
   share itself is unaffected).
5. `npm run build` (tsc) of `@spaarke/ui-components` fails in this worktree on 9 pre-existing errors, all from sibling
   packages not built here (`@spaarke/auth`, `@spaarke/sdap-client`); none is in a changed file. Jest: 79/79 for the
   AccessGrantModal suites; eslint clean on the changed files (one pre-existing warning).

## 10. Step 9.5 quality gates (code-review + adr-check)

**Code review findings and dispositions** (coverage-first; severity / confidence):

| # | Finding | Severity | Disposition |
|---|---|---|---|
| R1 | A reconcile run read the roots early; a root UNSHARE landing mid-run could be undone by a stale grant (over-share until the next tick) | Warning / high | **Fixed**: every grant or widening is re-checked against the roots read fresh just before the write (`FreshDesiredAsync`); test `ARootUnshareThatLandsMidRun_IsNotUndoneByAStaleGrant`, P20. Residual window: between the fresh read and the write (milliseconds) |
| R2 | An unshare reported 500 when a HELD child existed, although the held child does lose the user (it is narrowed) | Warning / high | **Fixed**: an unshare is incomplete only for children not updated at all; share messages name held children separately (`childrenHeld`); tests + P19 |
| R3 | Every `/share-user` on a secure root loads ALL Secure-team-owned rows of the environment (one paged query per table) and computes their lineage | Warning / medium (scale) | **Fixed in r1** (verifier F9): the scoped run walks DOWN from the root (§4 step 3); the reconcile's own cost is recorded in §11 |
| R4 | A lineage FAULT on an unrelated secure child makes a scoped fan-out report incomplete | Suggestion / medium | **Fixed in r1** (verifier F4): unrelated rows are never read in a scoped run; a faulted candidate counts in scope too, so N ≤ M |
| R5 | For an ORDINARY record whose fan-out cannot read the Secure team (Dataverse fault), the 500 says related records could not be read | Suggestion / medium | **Fixed in r1** (verifier F7): the root's own owner team answers an ordinary record first; residual in §11 |
| R6 | The Secure team id is resolved with the same two queries as `RecordOwnershipResolver`'s private helpers (small duplication) | Suggestion / high | Accepted: two queries; exposing the resolver's internals would widen 146's surface |
| R7 | `SecureChildShareSynchronizer.cs` is large (~750 lines) | Info | Cohesive single responsibility (one run's read → lineage → diff → write pipeline, state per run); not decomposed (COMPONENT-COMPLEXITY.md) |

No Critical findings. No secrets, no new endpoint, no CRUD→AI dependency, no package.

**ArchTests caught two more, both fixed:** `PoaShareClientSingletonGuardTests` (the synchronizer's log labels were the
quoted POA action names — renamed to `grant`/`modify`/`revoke`; the file builds no payload) and
`DocumentLinkVocabularyGuardTests` (the lineage map re-listed `sprk_document`'s record links — now derived from the ONE
declaration, `Spaarke.Dataverse.DocumentLinkFields`, minus the non-filing targets, written down).

**Suite results (2026-10-03):** BFF unit suite 14,486 — 14,429 passed, 54 skipped, 3 failed; the 3 (two Compose seam
tests and `EndpointGroupingTests` `/api/me`) each hit a 3-minute timeout while three other agents' suites ran on this
machine and pass on an isolated re-run (contention, not this change). That full run preceded the two ArchTest fixes
above; the affected suites were re-run after them (292: 290 passed, 2 pre-existing skips). ArchTests 372/372.

**ADR check:** ADR-001 (no new route; Minimal API handlers extended) ✓ · ADR-002 (no plugin; no schema change) ✓ ·
ADR-003 (fail closed on every unreadable read; no decision cached across requests — the per-run cache dies with the
run) ✓ · ADR-008 (existing `DelegationRuleFilter` gate unchanged; fan-out only after the gated root write) ✓ · ADR-010
(concrete singleton, no interface; registered in the feature module) ✓ · ADR-013 (no AI types) ✓ · ADR-019 (ProblemDetails +
stable reason code + trace id) ✓ · ADR-032 (unconditional registrations) ✓ · ADR-036 A1 (IScheduledJob +
AddScheduledJob, heartbeat every attempt, throws only when nothing could be decided; rule 3 claim not needed — the unit of
work is idempotent and read back) ✓ · ADR-038 (no `Mock<HttpMessageHandler>` — the wire tests reuse that file's
documented path-A `ScriptedHandler`; no DI-registration or ctor null-check tests; every guard seeded) ✓ · ADR-052 (BFF,
schedule) ✓. **No ADR conflict** (no §6.5 path needed).

## 11. Fix round r1 (2026-10-03) — the adversarial verifier's findings, item by item

Branch `task/uac-r2-149-r1` from `task/uac-r2-149` (`819e593f0`). Only the items below were changed.

| # | Finding | Disposition |
|---|---|---|
| 1-2 | Reproduced results; seeds that bit | Confirmed; nothing to change. |
| 3 | **F1** — the `Failed` outcome untested at both endpoints | **Closed.** Tests `ShareUser_WhenTheChildrenCannotBeReadAtAll_…_Failed_NotASilent200` (a child table unreadable) and `UnshareUser_WhenTheChildrenCannotBeReadAtAll_…_AndTheRootUnshareStands` (the root row unreadable): both answer 500 `children_incomplete` with `childrenStatus = Failed`; the root write stands. Seeds S1 (the verifier's `\|\| Status == Failed` in the share check) and S2 (Failed clause dropped from the unshare check): 3 failures (the share test, its F7 twin, the unshare test). |
| 4 | **F2** — the lineage-fault guard untested | **Closed.** `UnshareUser_WhenAChildsFilingCannotBeRead_Is500_NamingItInTheCounts_AndNothingIsWrittenOnIt` (a child of R also filed under R2, R2's row unreadable: 500, `childrenNotUpdated = 1`, nothing written on it) and `Reconcile_WhenAChildsFilingCannotBeRead_IsIncomplete_AndThatChildIsNotWritten`. Seed S3 (the verifier's: `notUpdated++` removed): 2 failures. New test-world fault `FailingRowReadsOf(table, id)` (one row's by-id reads throw). |
| 5 | **F3** — `DirectThreadAccessService` writes Read shares on `sprk_communications` (and `sprk_communicationthreads`), tables the synchronizer mirrors; the "shares ROOTS only" claim was false | **Closed.** (a) The message grant now reads the message's owner first and **skips a Secure-team-owned message** — its readers are the secure record's sharees, kept by the synchronizer; the participant set (anchor membership lookups + overlays) is not the root's share set, so granting it was a per-message over-share until the next tick and a fight between two writers. Unknown owner (message not found, Secure Record team ambiguous) → nothing granted (fail closed; a missed grant is that method's documented best-effort degradation); a Dataverse fault → the method's existing catch, nothing granted. (b) The Direct-thread share (`FindOrCreateDirectThreadAsync`) is on a thread created owned by the CALLER with no regarding record — never Secure-team-owned, so the synchronizer never touches it; commented at the call. (c) The false claim is corrected in the synchronizer header, §6 above and the POML. Uses the synchronizer's Secure-team resolution (made `internal static`, no new service, no new DI registration; the constructor gains `IConfiguration`, resolved by the container). Tests: `MessageAccess_OnASecureRecordsMessage_GrantsNoParticipant_AndTheReconcileGivesOnlyTheSharees` (participants A sharee + C non-sharee: nothing granted; one reconcile → A only, C never written), `…_OnAnOrdinaryTeamOwnedMessage_StillGrantsEveryParticipant`, `…_WhenTheSecureRecordTeamIsAmbiguous_OrTheMessageIsMissing_GrantsNothing`. Seeds S10 (guard removed): 2 failures; S10b (guard refuses everything): 5 failures (4 existing `DirectThreadAccessServiceTests` + the ordinary-message test). The existing unit tests now read a user-owned message by default. |
| 6 | **F4** — a lineage fault on ANY secure row counted against a scoped run; "N of M" with N > M | **Closed** with F9's change: a scoped run reads only the root's descendants, so an unrelated record's fault never reaches it (`ShareUser_IsNotAffectedByAFaultOnAnUnrelatedSecureRecord`), and a faulted candidate now counts in `ChildrenInScope` as well as `ChildrenNotUpdated` (N ≤ M by construction; the F2 unshare test asserts "1 of its 7"). Seeds S4 (scoped run loads the whole environment again): 2 failures; S11 (faulted rows not counted in scope): 1 failure. |
| 7 | **F5** — the fresh re-check before a grant tested with one root only | **Closed.** `ARootUnshareMidRun_OnEitherOfTwoRoots_IsNotUndoneByAStaleGrant` (theory over which root loses B; both roots' shares cached before the race, so the stale grant reaches the fresh re-check). Seed S6 (the verifier's last-root-only): fails for the root that is not last (1 case) — the theory guarantees a bite whichever order the lineage set has. |
| 8 | **F6** — the inherited-only (mask 0) exclusion and the batched read's `strict: true` untested | **Closed.** `Reconcile_IgnoresInheritedOnlyRows_NeitherRevokingThemNorModifyingThem` (a mask-0 row of a non-sharee is not revoked; a sharee holding only a mask-0 row is GRANTED, never modified) — seed S7 (`Where(s => true)`): 1 failure. Wire test `GetPrincipalAccessForRecordsOrThrowAsync_WhenARowHasNoReadableMask_Throws` — seed S8 (`strict: false`): 1 failure. |
| 9 | **F7** — an ORDINARY record answered 500 when the Secure team could not be resolved | **Closed.** A scoped run reads the root's own row first; not team-owned, or a team whose own row proves it is not the Secure team (another name, a default team, not an Owner team — rows the resolution can never select) → `NotApplicable`, the Secure Record BU never queried. `ShareUser_OnAnOrdinaryRecord_IsNotRefused_WhenTheSecureRecordTeamCannotBeResolved` (two BUs carry the name; 200, and `businessunit` is never queried) and its twin on a SECURE record (still 500 `Failed`, fail closed). Seed S9 (pre-check removed): 1 failure. **Residual:** a transient fault reading the ordinary root's own row (or its owner team's row) still answers 500 `children_incomplete` — the record cannot be told ordinary without a read, and fail closed wins. |
| 10 | **F8** — client: "Granted access to 0 of 1. The record was shared, but …" as an error | **Closed.** `children_incomplete` is no longer a policy refusal: the share counts as granted, the notice is a WARNING ("Granted access to 1 item(s). The record was shared, but …"), and the server's sentence is appended to whichever notice applies. Save stays open ONCE on such a result so the warning is read (it can name children an administrator must repair); nothing is staged any more, so the next Save closes it. Jest: the share test now asserts the text, the "Notice" title and no "0 of 1"/"Error"; new test for the Save behaviour. Seeds C3 (the verifier's state restored): 2 failures; C4 (Save closes at once): 1 failure. |
| 11 | **F9** — every scoped fan-out cost a full reconcile | **Closed for the request path** (the downward walk, §4 step 3): a `/share-user` or `/unshare-user` reads only the root's subtree — about one query per child table per level that has rows, plus the batched share reads of its own children — independent of the rest of the environment. Seeds S5 (user-owned rows not walked through): 1 failure; S12 (one level only): 9 failures; S5b (ordinary-team rows also walked through) did NOT bite — equivalent, not a hole: the upward walk still decides membership, so an extra candidate costs reads, never access. **The reconcile tick keeps a full sweep by design** (it is the net for writers the endpoints never see; POA changes do not touch a root's `modifiedon`, so no incremental filter exists). Its cost per tick ≈ 23 paged queries + one read per distinct parent (roots and intermediates, cached for the run) + ⌈children per table ÷ 25⌉ batched share reads + one share read per secure root + writes/read-backs for changed children only. E.g. 10,000 secure children under 500 roots ≈ 23 + ~500 + ~400 + 500 ≈ 1,400 requests per 2-minute tick (~3,500 per 5 minutes against Dataverse's 6,000-per-5-minutes-per-user service-protection limit). **Owner-visible with decision 3** (the cadence is code-fixed at 2 minutes; widening it widens the MDA-unshare window). |
| 12 | Process notes: Part A probes not run; unsecure does not fan out | **Not closable here.** The probes are live writes (G149-1, §8) — outside this session's authority. Unsecure: deliberately no fan-out (deviation 2) — revoking the children's shares before task 148 re-owns them would leave them readable by NOBODY; until 148 lands, the children of an unsecured record keep their old mirrored readers and the BU's users cannot read them. The goal's "after … unsecure" is therefore met only with task 148, which owns that transition (owner round 6 also keeps secured child roots secure). Recorded, not changed. |
| 13 | AC9 not met as proven | **Met in code + tests** after items 3, 4, 6 (Failed at both endpoints, lineage fault, honest counts). |
| 14 | AC1 — probe rows | **Pending live gate G149-1** (unchanged; no live writes). |
| 15 | AC5 — share survives an Assign | **Pending live gate G149-1 step 4** (unchanged). |
| 16 | AC4 — MDA share/unshare | Mechanism in code + tests; the 2-minute window awaits **owner decision 3**; live check **G149-2 step 4**. |
| 17 | AC12 — live gate with 146 | **Pending G149-2** (unchanged). |
| 18 | AC13 — publish size not reported | **Main session** (per this round's instructions; no package added, no new CVE surface). |

**#1081 (peer report, owner-decided 2026-10-02).** The dev root team holding Spaarke Basic User reads Secure Record rows
by depth for every root-team member; under round 5 that is an accepted dev finding and nothing is codified. The
synchronizer is unaffected: it writes only POA shares, never relies on role depth, and in production users sit in the
customer's child BU. The census keeps reporting it.

**r1 test results (2026-10-03):** affected suites (SecureChildShare*, SecureChildLineage, InternalUserShare*,
DataverseRecordShareWire, SecureProjectShare, ExternalAccessContract, DirectThreadAccessService*,
DirectThreadExplicitParticipantReader, CommunicationServiceMessageSend) 296/296; full BFF unit suite 14,502 — 14,448
passed, 54 skipped (pre-existing), 0 failed; ArchTests 372/372 (incl. `PoaShareClientSingletonGuardTests`,
`RecordOwnerAssignmentCensusTests`); jest AccessGrantModal 80/80; `@spaarke/ui-components` `npm run build` (tsc) shows
the same 9 pre-existing errors in unchanged files (unbuilt sibling packages `@spaarke/auth` / `@spaarke/sdap-client`),
none in a changed file; eslint on the changed client files: 0 errors (one pre-existing warning). Publish size: main
session.

## 12. Fix round r2 (2026-10-03) — the second verifier's findings, item by item

Branch `task/uac-r2-149-r2` from `task/uac-r2-149-r1` (`1a72f97d1`). **No production code changed.** The round adds
eight test cases that pin five fail-closed guards the verifier could remove with every test still green, one test-world
fault (`SecureChildShareWorld.EndlessPagesOf`), the guide's ship gate for unsecure, and the records below.

| # | Finding | Disposition |
|---|---|---|
| 1 | Reproduced: build clean, 283/283, ArchTests 372/372, empty remerge-diff, control seed bit | Confirmed; nothing to change. |
| 2 | **V6**: the fresh re-read failing (first root read OK) untested | **Closed.** `WhenTheFreshReReadOfTheRootFails_NothingIsGrantedOrWidened_ButRevokesAndNarrowingsStand`. A test double (`RootReadRace`) answers the root's FIRST strict read and throws on every later one. Children of R need grants (A, B), a widening (A on the event, View → Collaborate), a narrowing (B on the document, Full → View) and a revoke (C). Asserts: no GrantAccess, the widening is not written, the narrowing and the revoke ARE written, all 6 children "not updated", status Incomplete, and the root was read more than once. Seeds V6a (`grants.Clear()` removed: stale grants kept) and V6b (`modifies.RemoveAll(...)` removed: stale widenings kept): **1 failure each**. |
| 3 | **V1**: a widening ModifyAccess racing a root narrowing/unshare untested | **Closed.** `ARootChangeThatLandsMidRun_IsNotUndoneByAStaleWidening` (theory: `narrowed`, `unshared`). Every child of R already carries A at View and R gives A Collaborate, so the run plans ONLY widenings (no grant). `RootReadRace` narrows R's A to View, or revokes it, right after the run's first read of R. Asserts every child ends at View, or with no share, and no child write carries WriteAccess. Seed V1 (the verifier's: the re-check condition reduced to `grants.Count > 0`): **2 failures** (both cases). |
| 4 | **V2/V3/V4**: three "held" reasons untested | **Closed.** V2 `AChildWhoseRootIsMissing_IsHeld_AndEveryShareOnItIsRevoked`: a document whose only root does not exist keeps a former sharee's share. It is revoked, nobody is granted, `held = 1` and `outsideSecureRoots` stays 1. V3 `AChildWithAMissingIntermediateParent_IsHeld_OnlyNarrowedNeverWidened`: an attachment filed under a missing communication AND R's document. A is not granted, B is narrowed to View, C is revoked. V4 `AChildFiledDeeperThanTheWalkFollows_IsHeld_AndItsStaleShareIsRevoked`: a chain of `MaxLineageDepth + 1` events ending at R, built from the constant. The deepest event's stale share is revoked and nobody is granted; the event one level shallower IS mirrored, which pins the edge on both sides. Seeds V2, V3 and V4 (each "undetermined" assignment removed): **1 failure each**. |
| 5 | **V11**: the "unchanged" /share-user path's fan-out untested, though the 500 text says "or you can try again" | **Closed.** `ShareUser_RepeatedAfterAnIncompleteFanOut_CompletesTheChildren_ThroughTheUnchangedPath`. The first share fails on one child: 500, and the detail contains "try again". Repeating the SAME share, after the fault is cleared, answers 200 `outcome = unchanged` and every child, the failed one included, carries the share. Seed V11 (the verifier's: the unchanged path returns a plain 200): **1 failure**. |
| 6 | **V5**: the MaxPages ceiling untested | **Closed.** `AChildTableLargerThanThePageCeiling_FailsTheRun_AndNothingIsWritten`. The new world fault `EndlessPagesOf("sprk_todo")` reports more rows on every page. Asserts status Failed, nothing written, and exactly `MaxPages` page queries of that table. Seed V5 (the verifier's: return the rows read so far): **1 failure**. |
| 7 | **Unsecure transition gap**: owner round 7 item 5 (BINDING) says "kept in sync on ... secure/unsecure"; step 5 says wire "unsecure"; this task deliberately does not | **Not closable in code without an owner decision; now an explicit SHIP GATE plus a 🔔 owner decision (decision 4 below).** It is no longer only a deviation line. The guide §7a carries it as a 🔴 rule. |
| 8 | **Coordination with task 143** (complete on `task/uac-r2-143-r1` `d248dff11`, NOT merged into `work/unified-access-control-r2`; verified with `merge-base --is-ancestor`) | **An orchestrator obligation, recorded below** ("Merge-order obligation"). Nothing to wire on this branch: 143's guard type (`SecureShareNoAccessGuard`) and enforcer (`NoAccessShareEnforcer`) do not exist here. |
| 9 | Scale/budget of the reconcile (≈1,400 requests per tick at 10k children) | Already recorded (§11 item 11) and tied to owner decision 3. Restated in decision 3 below; no change (the cadence is code-fixed). |
| 10 | Verified correct | Confirmed; nothing to change. |
| 11 | Jest and the full suite not re-run by the verifier | Re-run this round: see "r2 test results". No client file changed in r2, so jest is r1's 80/80, not re-run. |
| 12 | AC1: the probe rows | **Pending live gate G149-1** (§8). No live writes from this session. |
| 13 | AC5: a share surviving an Assign | **Pending G149-1 step 4.** |
| 14 | AC4: owner acceptance of the 2-minute window and of the job shipping with writes on | **Pending owner decision 3.** |
| 15 | AC6: met only by its fallback clause | **Pending the merge-order obligation below.** 143 is complete but unmerged. |
| 16 | AC9 / ADR-003 fail-closed claims partly proven | **Closed** by items 2-6: V6, V1, V2, V3, V4 and V5 each now fail a test when removed. |
| 17 | AC3: "repeat the request completes it" | **Closed** by item 5 (V11). |
| 18 | The Goal's "after provisioning/unsecure" and owner R7-5 | **Ship gate + decision 4** (item 7). |
| 19 | AC12 | **Pending G149-2** (after G146-1 and the joint 146+149 deploy). |
| 20 | AC13: publish size | **Main session** (this round's instructions). No package added; no production code changed in r2. |

### Decision 4 for the owner: the unsecure transition — DECIDED (owner round 11 item 3, 2026-10-03): option (a)

> **Owner, round 11 item 3:** "ship gate. 146 and 149 may deploy, but no record is unsecured in a shared environment
> until task 148 (which re-owns the children, then calls `SyncRootAsync`) is deployed." Recorded in the guide §7a as a
> 🔴 ship gate and a "must NOT" row. The text below is the r2 record the decision answered.

- **Situation.** `/unsecure-project` moves the root out of the Secure team and revokes every share on it. The root's
  Secure-team-owned children are then "outside secure roots": the synchronizer leaves them untouched, so they keep their
  FORMER sharees. They stay that way until task 148 re-owns them into the record's business unit. With no 148, the
  window is unbounded.
- **Why not "keep in sync" literally.** After the unsecure the root's share set is EMPTY. Mirroring it would revoke every
  child share, and a memberless-team-owned child would then be readable by NOBODY: an under-share of the whole record's
  content, including for the people who just unsecured it.
- **Practical exposure.** In the production topology (round 5), former sharees in the customer BU can read the
  now-ordinary root by role depth anyway. The over-share is limited to former sharees OUTSIDE that BU (another BU's
  user, or a shared team). Meanwhile the BU's other users cannot read the children: an under-share.
- **Options.**
  - **(a) Ship gate (recommended).** 146+149 may deploy, but no record is unsecured in a shared environment until 148 is
    deployed (148 re-owns, then calls `SyncRootAsync`, and the children follow the ordinary root's BU).
  - **(b) Owner accepts the window in writing** for dev only (no production customer is affected before 148).
  - **(c) Pull 148's re-own into this task.** Rejected here: it is 148's scope (its probe (g) is unanswered) and the
    constraint "do not change anything else" binds this round.

### Merge-order obligation (for the orchestrator; AC6) — DISCHARGED in r3 (§13): 149 merged second and did all three

Task 143 (`task/uac-r2-143-r1`, `d248dff11`) is complete and unmerged. Its POML (:246, :268) says "149 must call this
task's guard ... and extend the enforcer to child shares, if 149 lands after this task". §5 of this note says "142 and
143 do". Each assumes the other lands second. **Whichever of 143 and 149 merges into `work/unified-access-control-r2`
SECOND must, in that merge's branch:**

1. Before any child GRANT or WIDENING in `SecureChildShareSynchronizer.MirrorAsync` (at the fresh re-check), drop every
   principal that `SecureShareNoAccessGuard` refuses for the ROOT.
2. Make `NoAccessShareEnforcer`, after it removes a root share, call `SyncRootAsync(root)` so the walled user leaves
   every child at once, rather than at the next tick.
3. Add a negative test: a walled user whose ROOT share has not yet been removed by the enforcer is never granted a
   child share.

Both branches also edit `InternalShareEndpoints.cs` and `RecordShareLevels.cs` (a textual conflict is expected). Until
this is done, a walled user's child shares follow their ROOT share: they are removed one tick after 143's enforcer
removes the root share, and never granted while 143 refuses the root share. The only gap is the interval before the
enforcer acts.

### Decision 3, restated — DECIDED (owner round 11 item 2, 2026-10-03): the window, the posture and the cost accepted together

> **Owner, round 11 item 2:** "a window of at most 2 minutes is accepted for MDA Share/Unshare and for NEW or
> RE-FILED children to pick up the root's sharees. The scheduled reconcile is the mechanism, and it ships with writes
> on. Dataverse's table-wide Share/Unshare/Reparent cascade is NOT enabled." The text below is the r2 record.

- **The window.** ≤ 2 minutes for MDA Share/Unshare and for new or re-filed children.
- **The job posture.** It ships with writes on.
- **The cost.** ≈ 1,400 requests per tick at 10,000 secure children under 500 roots, ≈ 3,500 per 5 minutes. That is
  against the app user's ~6,000-per-5-minutes service-protection budget, which the rest of the BFF shares.
- **What must be accepted together.** The cadence is fixed in code, and a longer cadence widens the MDA-unshare window,
  so the owner accepts all three together or chooses an alternative (§5 trigger 4).

**r2 test results (2026-10-03):** affected suites (`SecureChildShare*`) 63/63 (55 + 8 new cases). Seed-and-bite: V6a 1,
V6b 1, V1 2, V2 1, V3 1, V4 1, V5 1, V11 1 failures, each passing again after restore (file touched; `git status`
clean on `src/`). A first V2 seed did not compile (CS0642, empty statement) and was redone as an empty block. Full
BFF unit suite 14,510: 14,456 passed, 54 skipped (pre-existing), 0 failed. That is r1's 14,502 plus the 8 new cases.
ArchTests 372/372, including `PoaShareClientSingletonGuardTests`. No client file changed in r2.

## 13. Fix round r3 (2026-10-03) — merged with task 143; the third verifier's findings; owner round 11

Branch `task/uac-r2-149-r3` from `task/uac-r2-149-r2` (`8a0527fd0`), with `work/unified-access-control-r2`
(`6b243f092`, owner round 11) and `task/uac-r2-143-r2` (`7668bbc1f`, verified, ready to merge) merged in. **baseSha
(HEAD right after the merges) = `57e14adc4`.** This branch therefore lands SECOND of 143/149, and the merge-order
obligation (§12) is discharged here.

### 13.1 The merge (commit `57e14adc4`)

Ten conflicts, each resolved keeping both sides' intent:

| File | Resolution |
|---|---|
| `RecordShareLevels.cs` | 149's `ChildMirrorableMask` / `ChildMirrorMask` / `ChildMirrorRights` AND 143/133's `AllShareRights` / `MaskForRightsCsv` / `RightsCsvForMask` |
| `InternalShareEndpoints.cs` | both reason codes (`children_incomplete`; `subject_no_access`, `no_access_unverifiable`); `/share-user` takes the synchronizer AND the guard — the guard runs before any share read or write, the child fan-out after the confirmed root write |
| `ExternalAccessModule.cs` | both scheduled jobs (secure-child reconcile every 2 min; No Access every 5 min) |
| `DataverseCreateRecordHandler.cs` | **146's G5 create-as-the-app vs 133's interim creator stamp** — decided by owner round 10 ("superseded at integration by task 146's create-as-the-app, which writes the stamp in the create payload"; 133 note §13.8). The owned create of `sprk_project` / `sprk_matter` / `sprk_workassignment` now carries `sprk_createdbyperson` = the caller (`WhoAmI()` under their own token), re-mapped through metadata as a SERVER-set lookup (`WithCreatorPersonAsync`); 133's follow-up app-only update and its `IGenericEntityService` dependency are gone; the refusal of an item that names the column is kept (pre-suspend and on execute). `OwnedChildWrite.CheckCallerMayCreateAsync` exempts ONLY that server-set column from the field-security question (it is field-secured precisely so that only the BFF writes it). Schema not deployed → created without it, logged (133's non-fatal posture) |
| `InternalUserShareTests`, `BusinessSliceDeterminismContractTests`, `P2LoopInjectionEvalSuiteTests`, `DataverseToolNameFreezeTests`, `DataverseCreateRecordHandlerTests`, `ProvisionProjectTestFixture` | constructor shapes from 146 / both endpoint parameters / both fixture members; the three run-as-user stamp tests (obsolete: those tables take the owned path) replaced by owned-path tests in `SecureChildOwnershipAiToolTests` |

Semantic merge conflict found by the ArchTests (fixed in a follow-up commit, §13.3): task 133 renamed
`ProvisionProjectEndpoint.AssignOwnerToSecureTeamAsync` to `MoveOwnerAsync`; task 146's `RecordOwnerAssignmentCensusTests`
listed the old name — the census entry now names `MoveOwnerAsync` (kind Root: the move to the Secure team and the
compensating move back).

Merge-only compile fixes: `NoAccessShareEnforcerTests.InterleavingShares` gains the batched strict read (149's seam
method); `SecureChildShareMirrorTests` passes a guard that walls nobody to `/share-user`
(`SecureChildShareWorld.NobodyWalled()`).

New tests (`SecureChildOwnershipAiToolTests`, +6 cases): the three roots carry `sprk_CreatedByPerson@odata.bind =
/systemusers(caller)` in the app's own create with the column field-secured, no POST and no PATCH as the user; a child
table is never stamped; a request-named field-secured column on a root is still refused (the exemption is the creator
column only); the column not deployed → created without it, the "for" column kept. Seeds: **M1** stamp call removed → 3
fail; **M2** FLS exemption removed → 4 fail; **M3** column-not-mapped made fatal → 1 fails.

### 13.2 Items

| # | Item | Disposition |
|---|---|---|
| 1 | Verifier open items (`b4c-findings.json` "149") | Findings 1-3 below; AC6 below; AC4 owner part below. **Still pending for the main session:** live gates G149-1 (AC1, AC5) and G149-2 (AC12), and the publish size (AC13). |
| 2 | **Merge-order obligation with task 143 (AC6)** | **Done, all three.** (1) `SecureChildShareSynchronizer.MirrorAsync`, after the fresh re-check: every SYSTEM USER still to be granted or widened is asked about through `SecureShareNoAccessGuard.CheckAsync` for EACH of the child's secure ROOTS (cached per run); refused — walled, or unverifiable (ADR-003) — means no grant, no widening, only the narrowing part of the change (`NarrowingPartOnly`); unverifiable also leaves the child not updated (retried). Teams are not asked: an entry cannot name a team, and a team share is never the wall's (owner N2). (2) `NoAccessShareEnforcer` calls `SyncRootAsync(root)` after it removed a share on a record (`SyncChildrenAsync`); an incomplete fan-out is a `children-incomplete` failure naming the record (the root removal stands; the 2-minute reconcile completes it); nothing removed → no fan-out. (3) Negative tests below. The guard is scoped, so the synchronizer is now **scoped** (every consumer already resolves it from a scope). Cost: the check runs only when something is to be ADDED, once per (root, user) per run (cached), so the steady-state reconcile tick adds no request; each check is task 143's few app-only reads (flags, the user's link/binding, memberships, referenced organizations, the deny list). |
| 3 | **Finding 1** — `heldBack ? mask & want : want` unproven | **Closed.** `AHeldChild_WhoseShareIsNarrowerThanTheKnownRootsAllow_IsNeverWidened`: B holds View on a document filed under R and under a flagged-but-not-isolated project; R gives B Collaborate; B stays View, nothing is written on the held child. Seed **S1** (`var target = want;`) → it fails. The two older `…OnlyNarrowedNeverWidened` tests keep their names; the "never widened" half is now this test's. |
| 4 | **Finding 2** — a MIXED modify dropped whole when the fresh re-read fails | **Fixed.** On that path every change is cut to its narrowing part (mask AND what the share carries): a pure widening is dropped, a mixed one keeps the rights it removes, one left with no Read becomes a revoke. Test `WhenTheFreshReReadFails_AMixedChange_KeepsItsNarrowingPart_AndDropsOnlyItsWideningPart` (A holds Read+Delete on the event, R gives Collaborate: Delete goes, Write/Append/AppendTo are not added, one ModifyAccess to ReadAccess, run Incomplete). Seed **S2** (the old `RemoveAll`) → it fails. The same helper serves the No Access refusal (item 2). |
| 5 | **Finding 3** — §5 said "142 and 143 do" wire the guard | **Fixed.** §5 "Other root-share writers" now says this task wired it (149 merged second) and why deriving from the root was not enough. §12's obligation and decisions are marked discharged / decided. |
| 6 | **Owner round 11 items 2-4** | **Recorded** in this note (header, §1, §5 rows 2/4/5/6 + job posture, §12 decisions 3 and 4), the POML (r3 outcome) and the guide (§7a: the rule, the No Access rule, the accepted 2-minute window, the ship gate; must-NOT rows), plus the write-path I-2 row and three code comments. **AC4's owner part is CLOSED**: the window, the writes-on posture and "no platform cascade" are owner-accepted; the deployment-gating constraint's condition is met (a scheduled reconcile AND the owner's written acceptance). Its live check remains G149-2 step 4. |

**New negative tests (AC6, `SecureChildShareMirrorTests`):** `AWalledUser_WhoseRootShareIsNotYetRemoved_IsNeverGrantedAChildShare`
(theory: the root's fan-out and the reconcile; B walled on R and still shared on R → no child write for B, A mirrored,
B's root share untouched, run Completed); `WhenAWalledUsersGrantWasTheOnlyChange_NothingIsWritten_AndTheChildrenCountAsUnchanged`;
`AWalledUser_IsNeverWidenedOnAChild_ButANarrowingStillApplies` (View stays View; Read+Delete → Read);
`WhenTheNoAccessCheckCannotBeAnswered_NothingIsGivenToThatUser_AndTheChildrenAreNotUpdated`. **Enforcer
(`NoAccessShareEnforcerTests`):** `Enforce_ARemovedRootShare_RemovesTheWalledUserFromEveryChildAtOnce_NotAtTheNextTick`,
`Enforce_WhenTheChildrenCannotBeUpdated_IsAFailureNamingTheRecord_AndTheRootRemovalStands`,
`Enforce_WhenNothingWasRemovedOnTheRecord_DoesNotReadItsChildren`. The guard in these tests is task 143's REAL guard
over its documented doubles (flags, deny-list seam, identity row store).

**Seed-and-bite (r3; each failed, then passed after restore, file touched):**

| Seed | Mutation | Failed |
|---|---|---|
| S1 | held child: `target = want` | 1 (finding 1 test) |
| S2 | fresh failure: drop a mixed change whole (the old `RemoveAll`) | 1 (finding 2 test) |
| S3 | the No Access guard never consulted | 4 |
| S4 | an unverifiable check does not fail the child | 1 |
| S5 | unverifiable treated as not walled | 1 |
| S6 | a refused principal keeps its widening | 1 |
| S7 | a refused principal loses its narrowing too | 1 |
| S8 | the enforcer does not fan out after a removal | 2 |
| S9 | the enforcer reports an incomplete fan-out as complete | 1 |
| S10 | the enforcer fans out with nothing removed | 1 |
| S11 | a child with nothing left to write counted as "updated" | 1 |

**Deviations.** None from owner decisions. One behaviour refinement beyond the four items, needed by item 2 and pinned
(S11): a child whose planned writes are all dropped (a walled grant was the only change, or the fresh re-check removed
everything) is counted unchanged, not "updated" with no write behind it. Task 143's own note (§ "Task 149 child-share
fan-out ... HOOK for 149") and POML (:246, :268) still describe the hook as future work; they are 143's files and were
not edited — the main session may point them at this §13.

**`.claude/**` edits needed:** none.

### 13.3 r3 test results (2026-10-03)

- **Merge state** (`57e14adc4`), the resolved files' suites (RecordShare*, InternalUserShare*, DataverseCreateRecordHandler,
  BusinessSliceDeterminism, P2LoopInjectionEval, Provision*, DataverseToolNameFreeze, SecureChildOwnershipAiTool,
  NoAccess*, SecureChildShare*, RecordCreatorPerson*, SecureProjectShare, SecureShareNoAccessGuard,
  ExternalAccessContract): **608/608**.
- **r3** (`e7e7eec8b`), affected suites (the above + DirectThread*, SecureChildLineage, UnsecureProject): **827/827**.
- **Full BFF unit suite** (`dotnet test tests/unit/Sprk.Bff.Api.Tests`): **14,742 total = 14,623 passed + 54 skipped
  (pre-existing) + 65 failed**. The run took **37 minutes** while other agents' suites ran on this machine; the 65 failures
  (64 distinct names — Office save/quick-create, Compose seam, Insights/Workspace/Config contract, ProvisionNoAccess and
  similar host-based tests) were re-run in isolation with the same build: **115/115 passed** (the name filter matches 115
  cases). Contention, not this change; none of the 65 is in a file this round touched.
- **ArchTests** (`dotnet test tests/Spaarke.ArchTests`): first run **371/372** — `RecordOwnerAssignmentCensusTests`
  ("every owner write in the server is censused") failed on a **semantic merge conflict**: task 133 (via 143-r2) renamed
  `ProvisionProjectEndpoint.AssignOwnerToSecureTeamAsync` to `MoveOwnerAsync` (it now serves the compensation too),
  and task 146's census (via this branch) still listed the old name. Fixed by re-pointing the census entry (kind Root,
  reason extended); re-run **372/372** (incl. `PoaShareClientSingletonGuardTests`). Left alone (133's file, not this
  task's): a stale `<see cref="AssignOwnerToSecureTeamAsync"/>` in `ProvisionProjectEndpoint`'s remarks (line 126; XML
  doc warnings are off, so it does not fail the build) and the same old name in a `RecordOwnershipResolver` comment.
- **Integration suites (project hard gate):** `tests/integration/Sprk.Bff.Api.IntegrationTests` **104/104**;
  `tests/integration/Spe.Integration.Tests` **428 total = 403 passed + 25 skipped, 0 failed**.
- **Client:** no client file changed in r3 (the merge brought 143-r2's `CreateProjectWizard` / `SummarizeFilesWizard`
  changes, which do not overlap 149's `AccessGrantModal` change; nothing was resolved there), so no client build was run.
- **Publish size:** not measured (main session).

## 14. Fix round r4 (2026-10-03) — the fourth verifier's findings, item by item

Branch `task/uac-r2-149-r4` from `task/uac-r2-149-r3` (**baseSha `35da930fc`**). **No production code changed** (one
comment in `SecureChildShareReconciliationJob.cs`). The round adds one 4-case theory, a binding merge-order obligation
for task 142, and two documentation sentences (the multi-root rule and the Secure-flag edge of the wall).

| # | Item | Disposition |
|---|---|---|
| 1 | Reproduced r3 results (ArchTests 372/372, both integration suites, the full unit suite with 104 contention failures passing 141/141 in isolation, POML well-formed, no package) | Confirmed; nothing to change. |
| 2 | Seeds S1 and S2 bite (verifier findings 1 and 2 closed) | Confirmed; nothing to change. |
| 3 | **Finding A** — `WallAsync` asks about EVERY secure root, but no test pinned it: seed B (`break;` after the first root) left all 223 tests green, and a two-root probe gave the walled user the child | **Closed.** New theory `AUserWalledOnOnlyOneOfAChildsTwoSecureRoots_IsNeverGrantedThatChild` (`SecureChildShareMirrorTests`), 4 cases = walled on {R, R2} × trigger {fan-out, reconcile}. A document is filed under R (`sprk_project`) and R2 (`sprk_relatedproject`); A and B hold View on both roots; B is walled on ONE root only (task 143's REAL guard over the seam deny list, `DenySystemUserOnRecord(UserB, <that root>)`). The fan-out runs from the root that does NOT wall B (the adversarial direction). Asserts: B gets no share on the two-root document and no write names B there; A gets it at View (the document is otherwise mirrored); B still gets the open root's own single-root child (the wall is per record) and never the walled root's own child; run Completed. A theory over BOTH roots, as F5's was, so a mutation that checks only the first OR only the last root bites whichever order the lineage set has. **Seeds (each restored from a byte copy, file touched, `git status` clean on `src/`):** **B** `break;` after the first root → 2 failures (the two `R2` cases); **C** the per-run cache keyed on the USER alone (`_walls[(default(RowRef)!, user)]`) → 4 failures (all four cases: an answer cached from one root, or from an earlier child, is served for the other root); **D** only the LAST root checked (`.TakeLast(1)`) → 2 failures (the two `R` cases). In every seed the other 223 tests of the affected suites stayed green, which confirms the gap was real: only this test pins the multi-root half and the (root, user) cache key. |
| 4 | **Finding B** — AC6's task-142 half recorded only as optional ("142 calls `SyncRootAsync` ... if it wants them at once") | **Closed as a BINDING merge-order obligation** (below), §5's sentence replaced, and the job's remarks (`SecureChildShareReconciliationJob.cs`) no longer say "tasks 142 and 143 call the synchronizer when they land" (143 does since r3; 142 MUST, per the obligation). **Partly corrected from code:** at `task/uac-r2-142-r3` (`d48f5191e`; `task/uac-r2-142-r4` is at the same commit), the materializer never removes or narrows a share on a root whose `sprk_issecure` reads true: `EndAssignmentAsync` answers `KeptSecureRecord` before `RemoveOrRestoreShareAsync` (`:1174-1178`, owner S5), and the grant→share conversion requires `!flags.IsSecure` (`:652`). So "once 142 revokes an auto-share, the children keep that user" cannot happen on a FLAGGED secure root. It CAN happen on a Secure-team-OWNED root whose flag reads No/empty (item 7), where 142 revokes as on an ordinary root while the synchronizer still treats the root as secure. And 142's one write on a flagged secure root, the RESTORE of a share after a No Access wall is lifted (`FreshShareAsync` → `WriteShareAsync`, `:986-1016`), reaches the children only at the next tick: an under-share window (the fail-closed direction) that owner round 11 item 2 did NOT accept (it covers MDA Share/Unshare and new/re-filed children only). The POML constraint ("cover every BFF path that writes or removes a POA share on a secure root, including task 142's systemuser auto-share"; "whichever ... lands second does the wiring and its test") binds every write. The obligation therefore covers grants and removals alike. |
| 5 | Merge integrity: no dropped behaviour | Confirmed; nothing to change. |
| 6 | Verified correct in r3 | Confirmed; nothing to change. |
| 7 | **LOW** — the guard answers `NotSecure` when the root's `sprk_issecure` reads false/null (`SecureShareNoAccessGuard.cs:148-151`; null reads false, `ExternalParticipationService.FlagsFrom`), while the synchronizer counts a root as secure by its Secure-team OWNER (`SecureChildShareSynchronizer.cs:582`; the flag only makes a non-isolated root "held", `:584`) | **Recorded** (no behaviour change requested): §4 step 7 above and guide §7a ("The No Access list wins") now say it. A Secure-team-owned root whose flag reads No/empty (only part way through an unsecure, which the ship gate forbids in shared environments, or a hand edit before task 150's FLS lock) is not walled for its children. Task 143's enforcer (`NoAccessShareEnforcer.cs:417-419`, `NotSecure`) and the `/share-user` guard skip that root the same way, so a child is never wider than its root. |
| 8 | **LOW** housekeeping in other tasks' files | **Not changed here** (other tasks' files; editing them on this branch would only add integration conflicts). Still present at this branch: `ProvisionProjectEndpoint.cs:126` (`<see cref="AssignOwnerToSecureTeamAsync"/>`, 133's remarks; the method is `MoveOwnerAsync`) and `RecordOwnershipResolver.cs:862` (comment naming `ProvisionProjectEndpoint.AssignOwnerToSecureTeamAsync`). Task 143's note ("HOOK for 149") and POML (`:246`, `:268`) still describe the 149 hook as future work; it was done in r3 (§13). For the main session at integration. |
| 9 | AC6 partly not met | **Multi-root half: CLOSED** (item 3). **142 half: met by AC6's own fallback clause** ("if those tasks have not landed, the note records which task wires it"): 142 has not landed, and the note now records the wiring as a binding obligation on whichever merges second. Fully met when that merge lands with its tests. **143 half:** met since r3 (confirmed by the verifier). |
| 10 | AC1: Part A probe rows | **Pending live gate G149-1** (§8), unchanged. Live writes are outside this session (read-only). G149-1 has no precondition; owner round 11 approved it for the main session. |
| 11 | AC5: does a share survive an Assign (probe g)? | **Pending G149-1 step 4**, unchanged. |
| 12 | AC12 | **Pending G149-2.** Of its preconditions, G146-1 (role 9 → 26) is DONE on dev (2026-10-03, `work/unified-access-control-r2` `92d5f3cc1`, `notes/batch4-live-gates-2026-10-03.md`); the joint 146+149 deploy is still to come. |
| 13 | AC13: publish size | **Main session** (this round's instructions). No package or csproj change in r4; no production code changed. |

### Merge-order obligation with task 142 (for the orchestrator; AC6's 142 half) — BINDING

**Facts** (read 2026-10-03 at `task/uac-r2-142-r3` `d48f5191e`; `task/uac-r2-142-r4` is at the same commit; neither is
in `work/unified-access-control-r2` nor in this branch):

- `AssignedAccessMaterializer` (registered **Scoped**, `ExternalAccessModule.cs:227` on 142) writes system-user POA shares
  on ROOTS through the one seam: `WriteShareAsync` → GrantAccess `:1354` / ModifyAccess `:1357`, and
  `RemoveOrRestoreShareAsync` → RevokeAccess `:1393` / ModifyAccess `:1395`. Neither the class, its tests nor 142's POML
  mention `SyncRootAsync` or task 149.
- Its callers: the inline L1 trigger after a BFF writer's commit, the sync endpoint (form post-save, wizards, the
  "Update Access" ribbon), and `AssignedAccessReconciliationJob`. All three resolve it from a scope.

**Whichever of 142 and 149 merges into `work/unified-access-control-r2` SECOND must, in that merge's branch:**

1. Give `AssignedAccessMaterializer` a `SecureChildShareSynchronizer` constructor dependency. Both are Scoped; there is
   no cycle (the synchronizer depends on `IGenericEntityService`, `IDataverseRecordShareService`,
   `SecureShareNoAccessGuard`, `IConfiguration` and a logger, never on the materializer).
2. At the end of `MaterializeAsync`, if this run made at least one CONFIRMED system-user share write on the root (a
   `WriteShareAsync` that returned the confirmed mask after a GrantAccess/ModifyAccess, or a `RemoveOrRestoreShareAsync`
   that returned `true`), call `SyncRootAsync(run.Logical, run.RootId, ct)` ONCE for the root, not once per subject.
   With no confirmed share write there is no call, and the children are not read. On an ordinary root the call answers
   `NotApplicable` after reading the root's own row.
3. If the result is not complete (`!IsComplete`: `Incomplete` or `Failed`), or the fan-out throws (catch it), record a
   run failure (e.g. `run.Fail(null, "children-incomplete", ...)` naming the counts), so the job reports `Success = false`.
   The root write STANDS: it is never rolled back, and the 2-minute reconcile completes the children. This is the shape
   of the enforcer's `SyncChildrenAsync` (r3).
4. Add tests, each seeded to bite:
   - (a) a 142 share REMOVAL on a root the synchronizer treats as secure (Secure-team-owned; with the flag reading No,
     the only state in which 142-r3 removes one there) removes the user from every child in the same call, not at the
     next tick;
   - (b) the RESTORE after a lifted wall on a flagged secure root gives the user every child in the same call (the
     guard is consulted, as for every child grant);
   - (c) an incomplete fan-out is a failure of the run, and the root write stands;
   - (d) a run with no confirmed share write does not read the children.

Until this is done, 142's root-share writes reach the children at the next reconcile tick (≤ 2 minutes): a restore is
late (an under-share), and a removal on a Secure-team-owned root whose flag reads No leaves the user on the children for
up to 2 minutes. The No Access guard still applies to every child grant either way.

**For the main session:** add a "142 × 149" line to `notes/batch4-integration-steps.md` (Code reconciliation), like the
"143 × 149" one, pointing here. That file is on `work/unified-access-control-r2`, not on this branch.

**`.claude/**` edits needed:** none.

### 14.1 r4 test results (2026-10-03)

- **New theory alone:** 4/4.
- **Affected suites** (`SecureChildShare*`, `NoAccessShareEnforcer*`, `SecureShareNoAccessGuard*`, `InternalUserShare*`):
  **227/227** (the verifier's 223 + the 4 new cases); with seeds B / C / D: 225 / 223 / 225 passed (2 / 4 / 2 failures,
  all in the new theory); after restore: 227/227.
- **ArchTests** (`dotnet test tests/Spaarke.ArchTests`): **372/372**, including `PoaShareClientSingletonGuardTests` and
  `RecordOwnerAssignmentCensusTests` ("every owner write in the server is censused").
- **Integration suites (project hard gate):** `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests`
  **428 = 403 passed + 25 skipped, 0 failed**.
- **Full BFF unit suite** (`dotnet test tests/unit/Sprk.Bff.Api.Tests`): **14,746 = 14,671 passed + 54 skipped
  (pre-existing) + 21 failed**, in 35.7 minutes while other agents' suites ran on this machine. 14,746 is r3's 14,742
  plus the 4 new cases. The 21 failures (21 distinct names) are all host-based contract, seam or Office tests that ran
  about 3 minutes each (Insights, Office quick-create/save/search, Compose seam, drive-keyed route retirement, document
  profile, related-record card, memory pins, eval harness, workspace layout, endpoint characterization). None is in a
  file this round touched, and none is among the affected suites. Re-run in isolation on the same build:
  **22/22 passed** in 1.2 minutes (the name filter matches 22 cases). This is contention, not this change.
- **POML:** parses as well-formed XML.
- **Client:** no client file changed in r4. **Publish size:** not measured (main session). No package or csproj change.

## 15. Fix round f1 (2026-10-04) — the kept-container provisioning path did not fan out

Branch `task/uac-r2-149-f1`, base `6b685f0d4` (`integ/uac-r2-batch4`, after the 132 / 142 integration merges).

### 15.1 The item and its disposition

| # | Item | Disposition |
|---|---|---|
| 1 | **Integration residual (found merging 132, 2026-10-04).** `ProvisionProjectEndpoint` ran Step 8 (`SecureChildShareSynchronizer.SyncRootAsync`) only after Steps 6 + 7 created and recorded a NEW container. A record that keeps its OWN container (task 133 b2, `keptContainerId`) returned 200 from an earlier branch, so its Secure-team-owned children missed the shares Step 5.5 had just written until the next reconcile tick (≤ 2 minutes; owner round 11 item 2 accepted that window for MDA Share/Unshare and for NEW or RE-FILED children, not for a BFF root-share write). | **CLOSED.** Confirmed from code: the handler has exactly two successful returns — the kept-container `return TypedResults.Ok(…)` and the new-container one after Step 7; the resume path (`resume = true`) never sets `keptContainerId` (`!resume &&` guards the classification), so it always ends in the new-container path. Step 8 is now ONE private helper, `FanOutToSecureChildrenAsync` (same body as before: `SyncRootAsync(root.LogicalName, recordId)`, never fails the provisioning, warns on an incomplete fan-out for the scheduled reconcile), called on BOTH paths, after the Step 5.6 eviction and before the 200. Every successful provisioning now fans out exactly once. |

### 15.2 Tests (`tests/integration/data-mutation/ExternalAccess/SecureProjectShareTests.cs`)

- `Provisioning_FansTheNewSharesOutToTheSecureChildren_WhetherTheContainerIsKeptOrCreated` — theory, 6 cases
  (project / matter / work assignment × container kept / created). A Secure-team-owned `sprk_document` filed under the
  root gets exactly one share, to the creator, never with `ShareAccess`; the response's `speContainerId` and the count
  of created containers prove the case ran the path it names (kept: the record's own container, none created; created:
  the provisioned container, one created).
- `Provisioning_WhenTheChildFanOutCannotComplete_StillSucceeds_AndLogsTheIncompleteChildren` — theory, 2 cases (kept /
  created). The child table faults: provisioning still answers 200 with the right container, the synchronizer WAS asked
  on that path (`sprk_document` queried), the `secure children are not all in line` warning names the record, and no
  child share is written.
- The existing `Provisioning_FansTheNewSharesOutToTheRecordsSecureChildren` (new-container path, project) is unchanged
  and green.

**Seed-and-bite** (each restored from a byte copy, file touched, `SEED` absent afterwards, git clean on `src/`):

| Seed | Change | Result |
|---|---|---|
| A | the kept-path `FanOutToSecureChildrenAsync` call commented out (the pre-fix behaviour) | **4 failures** — the 3 kept cases of the 6-case theory + the kept case of the incomplete theory; 73 others green |
| B | the new-container-path call commented out | **5 failures** — the existing project test, the 3 created cases, the created incomplete case |
| C | the helper's incomplete warning suppressed (`if (false && …)`) | **2 failures** — both incomplete cases |

### 15.3 Test results (2026-10-04)

- **Affected suites** (`SecureProjectShareTests`, `ProvisionRecordedContainerTests`, `ProvisionProjectIdempotencyTests`,
  `ProvisionAssignCascadeChildOwnerTests`, `SecureChildShare*`): **219/219** (8 of them new), before and after the commit
  (the pre-commit `dotnet format` changed nothing).
- **Full BFF unit suite** (`dotnet test tests/unit/Sprk.Bff.Api.Tests`): **15,479 = 15,425 passed + 54 skipped
  (pre-existing), 0 failed** (16 m 47 s).
- **ArchTests** (`dotnet test tests/Spaarke.ArchTests`): **595/595**.
- **Integration:** `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **428 = 403 passed + 25
  skipped, 0 failed**.
- **Publish size (CLAUDE.md §10, measured here, not deferred):** both sides from FRESH trees (`git archive` of the base
  `6b685f0d4` and of the fix commit `813c929fe`, `src` + `config` + root props) at short paths (`C:\tmp\w149m`,
  `C:\tmp\w149b`), `dotnet publish -c Release`, **212 files on each side**, zipped with PowerShell `Compress-Archive
  -CompressionLevel Optimal` (the `Deploy-BffApi.ps1` method), incl. PDBs: base **46.00 MB**, branch **46.00 MB**,
  delta **−4 bytes**. No package or csproj change, so no new CVE surface.
- **Client:** no client file changed.

### 15.4 Placement, escalations, gates

- **Placement (CLAUDE.md §10/§11):** no new service, DI registration, endpoint, option, job, column, PCF or package. One
  new PRIVATE static helper inside the existing endpoint, extracted from the existing Step 8 so both successful paths
  call the same code (existing = Step 8's block; extension = call it from the kept path too, rather than a second
  copy that could drift; cost of doing nothing = a secure record provisioned with its own kept container leaves its
  secure children without the creator's and colleagues' shares until the next reconcile tick — an under-share no owner
  decision accepted). No Dataverse plugin (ADR-002); fail closed unchanged (ADR-003): the synchronizer's own rules
  decide every child write.
- **Escalations:** none fired; none open.
- **Live writes:** none. G149-1 / G149-2 (§8) are unchanged and need no new row: neither has a provisioning fan-out
  step, because live a record being provisioned normally has no Secure-team-owned children yet (task 148 re-owns
  existing ones and runs the same synchronizer itself), so Step 8 is a no-op there on either path. The behaviour this
  round fixes is pinned in-process by the 8 cases above, which seed such a child on each path.
- **`.claude/**` edits needed:** none.


> **Integration note (main session, 2026-10-04, merge of `task/uac-r2-149-f1`):** the kept-container fan-out above (`FanOutToSecureChildrenAsync`) is **superseded by task 148's Step 8** (`ChildrenFollowAsync`), which runs after the container steps on EVERY successful provisioning path (new or kept container) and reports an incomplete pass as 500 `children_incomplete` (ADR-003) instead of logging and succeeding. The method was not kept. 149-f1's tests were kept: they prove both container paths mirror, and the "cannot complete" case now asserts 148's `children_incomplete`.
