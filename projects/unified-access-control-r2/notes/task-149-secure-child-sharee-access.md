# Task 149 — people shared on a secure record see exactly its children (C10 part 2, sharees; #1071)

> Branch `task/uac-r2-149` (base `task/uac-r2-146-b2-r2` + `work/unified-access-control-r2` merged at `874bb1c2f`).
> Status: **code complete, not deployed.** Ships together with task 146. Live steps are manual gates (§8).
> Owner decisions that bind this task: round 7 item 5 ("each child's principals and rights equal the root's POA share
> set ... never wider"), round 5 (production topology), round 6 (secured child roots stay secure; task 158), round 3
> R3/R4 ("minutes, never hourly"), C4 (Write-holders share out of the box).

## 1. Outcome

Once task 146 owns every child of a secure project, matter or work assignment by the memberless `Secure Record Owners`
team, the only way to reach a child is a share on the child row. **Dataverse provides none** (§2), so the BFF now keeps
every Secure-team-owned child shared with exactly the internal principals its secure root is shared with:

| Rule | Implementation |
|---|---|
| Who | every SYSTEM USER and TEAM with a direct POA share on the root (inherited-only rows, mask 0, are not shares) |
| Rights | the root's rights restricted to Read, Write, Append, AppendTo, Delete — **never Share** (trigger 6 default), never Assign, never Create (`RecordShareLevels.ChildMirrorMask`) |
| Several secure roots | the **intersection**: a principal must be shared on every one, at the lowest rights (trigger 2 default) |
| Anything else on the child | revoked; a wider share is narrowed; a missing one granted |
| Which rows | rows of the 23 codified child tables owned by the Secure team, whose lookups lead (through Secure-team-owned or user-owned children, never ordinary-team-owned ones) to a Secure-team-owned root |
| Never touched | children of non-secure roots; rows owned by any other team; the roots themselves |

One component, `Services/Access/SecureChildShareSynchronizer.cs`, over the one POA seam. Three triggers:

1. **`/share-user` and `/unshare-user`** fan out in the request, after the root write is confirmed (also when the root
   was already right / already absent, so repeating a request completes it). Some children not updated → **500
   `sdap.access.user_share.children_incomplete`** with `childrenInScope`, `childrenUpdated`, `childrenNotUpdated`,
   `childrenHeld` and the root outcome; the root write stands (an unshare is never rolled back).
2. **Secure provisioning** (Step 8, after the container is recorded) — best effort, logged; normally a no-op today
   (task 148 re-owns existing children, then calls the same synchronizer).
3. **`SecureChildShareReconciliationJob`** — every two minutes, every secure child in the environment. This is the
   mechanism for new and re-filed children (about thirty BFF writer sites plus the client writers of task 147) and for
   out-of-the-box MDA Share/Unshare of a secure root.

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

1. Resolve the Secure Record owner team (BU by name TOP 2, then the named non-default Owner team TOP 2 — the
   resolver's rule). No BU or no team → `NotApplicable` (no secure records exist). Two of either → `Failed`.
2. Scoped run (`SyncRootAsync`): the root must be owned by the Secure team, else `NotApplicable` (nothing read below).
3. Load every Secure-team-owned row of the 23 child tables (one paged query per table, the lineage lookups selected).
4. Each row's secure roots: walk its lookups upward (cycle-safe, ≤ 6 levels). A Secure-team-owned root counts; an
   ordinary root contributes nothing; a missing parent, a root flagged `sprk_issecure` but not isolated, or a chain too
   deep makes the row **held**. A child owned by a user is looked through; one owned by an ordinary team is not.
5. Each root's mirror from the STRICT share read; a root that cannot be read → no write on any child that needs it.
6. Each child's shares from the new batched strict read (`GetPrincipalAccessForRecordsOrThrowAsync`, 25 per call); a
   batch that cannot be read → no write on those children.
7. Diff and write: revokes, then narrowings (ModifyAccess), then grants (GrantAccess only where no direct share exists —
   task 063's rule). A held child is only revoked/narrowed, never granted or widened. Anything that ADDS a right is
   re-checked against the roots read FRESH right before it is written, so a root unshare that lands mid-run (the
   endpoint's fan-out racing the reconcile) is not undone by a stale grant. Every changed child is read back.

## 5. Escalations (CLAUDE.md §6 / §6.5). None blocks the code; each is recorded with the default implemented.

| # | Trigger | State |
|---|---|---|
| 1 | A relationship cascades Assign | **Does not fire** (§2a). |
| 2 | A child under TWO secure roots with different sharee sets | **INTERSECTION implemented** (fail closed). Owner decision pending. Recommendation: keep the intersection — a child filed under P1 and P2 is reachable only by people trusted on both; the alternative (union) shows P1's document to P2-only sharees. |
| 3 | Mixed cascade + synchronizer | **Does not fire** (synchronizer only). |
| 4 | MDA Share/Unshare cannot be synchronized inside an accepted window | **Fires as a decision, not a stop.** An MDA Unshare of a secure root is an over-share until the next reconcile tick: **≤ 2 minutes** (the job's cadence). The owner's R3/R4 "minutes, never hourly" covers it; the deployment-gating constraint's condition is met by the scheduled job shipped here (it is "an interim schedule of the same job"; 147/148 reuse it, never duplicate it). Removing prvShare from ordinary roles is NOT proposed (it contradicts C4). **Decision 3 for the owner:** accept the ≤ 2-minute window for (i) MDA Share/Unshare and (ii) NEW and RE-FILED children (the creator, if shared on the root, may not open a child they just created for up to two minutes), OR choose the platform option: Share + Unshare + Reparent cascade on the secure-relevant relationships, which removes both windows — but see trigger 5. Recommendation: accept the window now; if UX testing shows the create latency matters, prefer a targeted inline mirror at the two or three interactive create paths (document upload, Office save, event/to-do create) over the platform cascade. |
| 5 | Enabling Share/Unshare/Reparent cascade is table-wide | **Not applied, not proposed for this task.** It would share every ORDINARY record's children with whoever the record is shared with — a product-wide behaviour change (e.g. a user outside the root's business unit would reach all its children). Owner decision only; the guide lists it as a "must NOT" until then. Also unproven live: whether a Reparent cascade gives inherited access on CREATE (probe b in G149-1, only if this option is chosen). |
| 6 | ShareAccess mirrored onto children | **Omitted** (recommended default): sharing happens at the root and fans out. Owner decision pending. |
| — | Job posture | The reconcile ships **enabled with writes** (unlike the report-only jobs of tasks 137/141): it IS the mechanism, and every write is bounded by the root's own shares. Owner may ask for report-only first; then 146 + 149 must not reach a shared environment until writes are enabled (the deployment gate). |

**Other root-share writers.** Task 142 (Assigned-To POA shares on roots) and task 143 (No Access for internal users on
secure records) have NOT landed on this branch (no guard type exists; `InternalShareEndpoints` remarks: "task 143's ...
not checked here"). Per the constraint, **whichever lands second wires it: 142 and 143 do.** What they need: after their
root-share write/removal call `SecureChildShareSynchronizer.SyncRootAsync` (or rely on the two-minute reconcile). Because a
child's sharees are by construction a subset of the root's, a walled user whose ROOT share 143 removes or refuses can
never hold a mirrored child share after the next sync — the "guard before child write" is satisfied by deriving from the
root (143 should still add a negative test: a walled user is never on a child).

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
| `SecureChildShareSynchronizer` (service + 1 DI line, concrete singleton) | `IDataverseRecordShareService` writes POA; every caller shares ROOTS only (grep `GrantAccessAsync(`: InternalShareEndpoints, ProvisionProjectEndpoint, PlaybookSharingService, DirectThreadAccessService). Nothing mirrors to children | Not in the seam (a pass-through testing seam, ADR-010); not in InternalShareEndpoints (provisioning + the job need it); not in the resolver (decides before the row exists) | after 146, every internal sharee of a secure record — its creator included — loses every document, event, to-do and communication on it; MDA unshares never reach the children |
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
   revoking the children's shares before task 148 re-owns them would leave them readable by nobody.
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
| R3 | Every `/share-user` on a secure root loads ALL Secure-team-owned rows of the environment (one paged query per table) and computes their lineage | Warning / medium (scale) | Accepted for now — one algorithm for scoped and full runs, bounded by 23 paged queries plus batched POA reads. Follow-up if secure volumes grow: a root-scoped downward walk over `SecureChildLineage` |
| R4 | A lineage FAULT on an unrelated secure child makes a scoped fan-out report incomplete | Suggestion / medium | Accepted (conservative: the faulted row might be under the root) |
| R5 | For an ORDINARY record whose fan-out cannot read the Secure team (Dataverse fault), the 500 says related records could not be read | Suggestion / medium | Accepted: the message is honest about what is unknown; the root share stands |
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
