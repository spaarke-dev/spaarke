# Scoping: protect Signals and Decision Records on secured matters (O-17 / D-15)

> **Date**: 2026-10-07 · **Read-only investigation** (no code, no Dataverse writes, nothing committed)
> **Sources**: `origin/master` @ `dae5869d2` (after uac-r2 batch 4, #1312, and task 171, #1333); spaarkedev1 read-only
> Dataverse queries made on 2026-10-07; this branch's `SignalWriter.cs`, `spec.md` §9 and `notes/security-roles.md`.
> **Question**: how much work is it to secure `sprk_signal` and `sprk_decisionrecord` on secured matters with the
> unified-access-control (uac-r2) mechanism, instead of D-15's skip?

## 0. Answer in one paragraph

**This is Medium work: about 3 dev-days (range 2.5–4), and most of it is registration and verification, not new code.**
The uac-r2 mechanism is already built and generic. To bring two more tables under it you:

- add two entries to the lineage map and two to the owner-role list;
- change the Signal writer in about 40 lines, the same change task 146 made to the spend-signal writer;
- make four privilege edits;
- run one live check.

uac-r2's logic does **not** change. Its owner has to accept edits to two files it owns.

**One finding changes the question.** D-15 skips **Restricted** and **Limited** matters, but those two settings govern
**external contacts only**. They do not restrict internal users at all. The setting that walls a matter off from
internal users is the **Secure** flag (`sprk_issecure`), and D-15 does **not** skip it. So, as written, R1 skips matters
that need no protection and leaves Secure matters unprotected (§1).

**Recommendation: do it in R1** (§6).

## 1. Correction to D-15: Restricted and Limited are not internal access controls

Evidence, all on `origin/master`:

- **The three Access Permission values.** `sprk_accesspermission` has three values: Standard `100000000`, Limited
  `100000001`, Restricted `100000002` (`ExternalParticipationService.cs:864-881`).
- **What each value does.** The doc comment on `RootRecordFlags` (`ExternalCallerContext.cs:142-160`) says:
  - Restricted *"removes every **contact-sourced** contribution"*;
  - Limited *"makes the record DIRECT-ONLY for **contacts**"*.
- **ADR-003 composition (lines 155-185).** The Restricted veto gives *"None for ALL contacts"*. The direct-only rule
  (Secure or Limited) suppresses contact terms, and *"internal ADR-034 membership is never suppressed"*.
- **The comment at `AccessibleRecordSetService.cs:605`.** Restricted means *"Only system users may have access"*.

**Consequence.** An internal user in the matter's business-unit tree can open a Restricted or Limited matter through
their role depth, exactly as for a Standard matter. A Signal that is BU-scoped to that matter therefore exposes nothing
those users could not already read. FR-14a's rationale ("a Restricted matter they cannot open") does not hold. O-20
(Decision Records on Restricted or Limited matters) does not arise either.

**What is actually unprotected.** A matter with `sprk_issecure = true` is owned by the memberless `Secure Record Owners`
team in the `Secure Record` business unit. Internal users reach it **only** through a share on the record. Today's
writer sets the Signal's `owningbusinessunit` to the matter's business unit, which for a Secure matter is `Secure
Record`, and leaves the writer as owner. The result:

- **In production** (users sit in a customer child business unit, a sibling of `Secure Record`), nobody can read the
  Signal, **not even the matter's own sharees**. That is an under-share. The uac share synchronizer mirrors only rows
  the Secure team owns, so the Signal is never mirrored.
- **In dev**, every root-BU principal can read it. The root default team `Spaarke` holds `Spaarke Console User` at
  Parent: Child BU depth, and `Secure Record` is a child of root. That is an over-share; uac accepted it as a dev
  artifact in #1081 / owner round 5.

O-17 asks exactly this. Live counts in spaarkedev1: 62 matters; **0 Secure**, 1 Limited, 1 Restricted.

## 2. How the uac-r2 secure-record mechanism works

### 2.1 Ownership

A secure root (project, matter or work assignment) and every child filed under it are owned by the **named, memberless
owner team `Secure Record Owners`** (`6eabc7f9-…`) in the **`Secure Record` business unit**. It is not the business
unit's default team (task 144). The team holds exactly one role, `Secure Record Owner`, which has **Read at User (Basic)
depth** on each codified table and nothing else.

That Read exists only because Dataverse refuses an owner whose roles lack Read on the table: `0x80040299 Read Privilege
Check For Owner failed`. This is the same refusal as task 030's F3. The `Secure Record` business unit is a child of root
and a sibling of the customer business units, so nobody reaches its rows through role depth.

### 2.2 Who decides the owner (invariant I-6)

`Services/Dataverse/RecordOwnershipResolver.cs` → `ResolveOwnerAsync(RecordOwnershipContext)` returns Owned, Refused or
Unchanged.

- **Record-first.** The owner comes from the business units of the parents, falling back to the acting user's.
- **Secure if any.** If any parent is owned in the `Secure Record` business unit, the named Secure team owns the row.
  Otherwise the primary parent's business-unit **default** team does.
- **Fail closed.** A root flagged secure but not isolated refuses (C11), and so does a missing parent or a missing team.

Background writers log a refusal and skip the row. `WP-1` registry row I-6 is in
`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.

### 2.3 Grants: created, kept in sync and revoked

The platform cascades nothing: every root→child relationship is `NoCascade` for Share, Unshare and Reparent. That
includes the existing `sprk_matter → sprk_signal` and `sprk_matter → sprk_decisionrecord` relationships (task-149 note
§2a). The BFF therefore keeps the shares itself, in `Services/Access/SecureChildShareSynchronizer.cs` (task 149):

- **What a child gets.** Every Secure-team-owned child is shared with exactly the users and teams that hold a direct
  share on its secure root.
- **Which rights.** The root's rights, limited to Read, Write, Append, AppendTo and Delete (`RecordShareLevels.ChildMirrorMask`).
  Never Share, never Assign.
- **Two secure roots.** A child under two secure roots gets the intersection of their sharees.
- **Everything else** on the child is revoked or narrowed.
- **No Access list.** Users on a secure record's No Access list (task 143) are never granted.

The synchronizer runs from four triggers:

1. **`/share-user` and `/unshare-user`** fan out inside the request, scoped to the root's descendants.
2. **Secure provisioning** fans out.
3. **`SecureChildShareReconciliationJob`** runs every 2 minutes over every Secure-team-owned child. This is how a
   newly created child picks up its sharees, and how model-driven-app Share/Unshare of a root reaches the children.
   Owner round 11 item 2 accepted a window of ≤ 2 minutes.
4. **`NoAccessShareEnforcer`** fans out after it removes a share.

### 2.4 Transitions (Make Secure, unsecure and backfill)

`Services/Access/SecureChildReconciler.cs` (task 148) re-owns a root's **existing** children, using the resolver's
answer and then `SyncRootAsync`. Its three triggers:

- **`/provision-project`** (which handles project, matter and work assignment) moves children **into** isolation.
- **`/unsecure-project`** moves them **out** to the business unit's default team and removes the mirrored shares.
- **`SecureChildReconciliationJob`** runs every 2 minutes. Its **recent-changes pass** (writes on) pulls into isolation
  any child created or re-filed under a secure root that is not yet Secure-team-owned. Its full sweep is report-only
  unless enabled.

### 2.5 What registers a table as a secure child

Three places, which tests keep in lockstep:

| Place | Role | Pinned by |
|---|---|---|
| `config/secure-record-owner-role.json` (embedded in the BFF) | The ONE list of tables the `Secure Record Owner` role covers (`kind: child`). Its `howToExtend` requires a **recorded live refusal** before adding a table. `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` applies it per environment (dry run, `-Apply`, `-Verify`) | `SecureChildLineageTests.TheLineageMapCoversExactlyTheCodifiedChildTables`; NFR-05 clause 5 in `SecureRecordIsolationCensusJob` (the live role must cover every codified table) |
| `Services/Access/SecureChildLineage.cs` → `Children` | Each child table's entity set and its lookups to roots or other children. Both the synchronizer and the reconciler walk these | `EveryLookupTargetsARootOrAMirroredChild` (no service request, no identity tables) |
| `RecordOwnershipResolver.OwnershipParentEntities` | Tables a child can be filed **under**. Only needed if something files under the new table | `EveryReparentableChildOfTheResolverIsMirrored`; `RecordOwnerAssignmentCensusTests` (the role set covers every parent and every routed census table) |

**What a writer must do when it creates a child** (task 146):

1. Call `ResolveOwnerAsync` with **every** parent lookup the row carries.
2. Put `ownerid` = the resolved team **in the create itself**, not as a later Assign.
3. On a refusal, refuse or skip in the writer's own error contract.

The writer does **not** grant shares (the 2-minute reconcile does) and does **not** set `owningbusinessunit`; it derives
from the team. The writer identity needs **Assign** on the table to create a row owned by someone else, and the owning
team's role needs **Read**.

## 3. Cost reference

There was no clean single-table PR: 21 of the 23 child tables were registered together inside #1312, the batch-4 merge
of 1,335 files. The two closest analogues:

| Reference | What it took |
|---|---|
| **`sprk_spendsignal`**: a background writer of a matter-scoped "signal" row, the nearest analogue. Task 146, inside #1312 | `SignalEvaluationService.cs`: **+34 lines** (inject the resolver, resolve once per matter, skip and log on refusal, `ownerid@odata.bind` in the upsert). Plus one census entry, one config entry with its live refusal, and the role privilege applied by script. `SpendSnapshotService.cs`: +43 lines, the same shape |
| **`sprk_invoice` joins the codified set**: task 145, `3367ef493` (#1046) | 11 files, +1,129 lines. Most of that was notes, a 132-line handoff and new census/assertion code built for the first time. The table entry itself was +8 lines of JSON, after a recorded live 403 probe |

**Takeaway.** Per table, registration is a few lines of data plus one live probe. The writer change is about 30–50
lines plus tests. The heavy machinery (synchronizer, reconciler, jobs, census) already exists and is table-agnostic.

## 4. What `sprk_signal` and `sprk_decisionrecord` need

### 4.1 Registration (uac-owned files; data only)

**`config/secure-record-owner-role.json`.** Add two `kind: child` entries:

- `sprk_signal`, with privilege `prvReadsprk_Signal`;
- `sprk_decisionrecord`, with privilege `prvReadsprk_DecisionRecord`.

Each needs a recorded refusal. Live today, the `Secure Record Owner` role holds neither privilege; it holds
`prvReadsprk_SpendSignal`, for example. Then run `Set-SecureRecordOwnerRolePrivileges.ps1 -Apply`, then `-Verify`, in
each environment.

**`SecureChildLineage.cs`.** Add two entries. The lookups below are from live `describe`:

- **`sprk_signal` → `sprk_signals`:**
  - `sprk_matter` → matter;
  - `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingworkassignment` → the roots;
  - `sprk_regardingcommunication`, `sprk_regardingtodo`, `sprk_regardingevent`, `sprk_regardinginvoice`,
    `sprk_regardingdocument` → children;
  - `sprk_decisionrecord` → the Decision Record (kept because an extra lookup can only narrow what a child receives);
  - **excluded**: `sprk_regardingservicerequest` (deliberately absent, per the file's remarks), `sprk_policy` and
    `sprk_policyversion` (not filing parents).
- **`sprk_decisionrecord` → `sprk_decisionrecords`:** `sprk_matter` → matter. It is the table's **only** parent lookup.
  `sprk_action` (→ `sprk_analysisaction`) and `sprk_policyversion` are not filing parents.

**`RecordOwnershipResolver.OwnershipParentEntities` needs no change.** Nothing is filed under a Signal or a Decision
Record. `RecordOwnershipContext.ParentsOf` already treats their parent lookups as ownership parents.

**Optional: `RecordOwnerAssignmentCensusTests.ChildTables`.** Adding the two tables would make the ArchTest enforce
resolver-routed creates. It would also pull in the `sprk_createdbyperson` stamping obligation (task 146 c1-r1), which
is a schema column on both tables. **Recommendation: leave this out of R1** and note it for uac.

### 4.2 Writer change (Signals)

In `SignalWriter.WriteAsync`, after `ResolveGroupingMatterIdAsync`:

1. Call `IRecordOwnershipResolver.ResolveOwnerAsync` with the matter and the subject as parents.
2. **If the answer is `IsSecureOwner`:** set `ownerid` to the Secure team. Do not set `owningbusinessunit`; it derives.
   Verify by reading back `owningteam`.
3. **Otherwise:** keep today's FR-14 path (owner = writer, `owningbusinessunit` = the matter's business unit).
   - Why keep it: a full I-6 switch to default-team ownership hits F3 (`0x80040299`). Most business units' default teams
     hold no role with Read on `sprk_signal`; that is why 59 of 62 matters failed.
   - Note: for a Secure matter today's path would put the row in the `Secure Record` business unit anyway, so the BU
     check in `EnsureOwningBusinessUnitMatches` keeps passing after the reconciler re-owns a row.
4. **Refused** (flagged but not isolated, Secure team unresolved): skip, log and count the subject. This is the spend
   signal's behaviour and FR-14a's style. Add one `OntologyWriterFailureReason`.

Size: about 40–60 lines plus unit tests.

**Can the service identity still create and close?** Yes. Live role privileges for `Spaarke Ontology Service` on
`sprk_signal`:

| Privilege | Depth | Note |
|---|---|---|
| Create | Organization | |
| Read | Organization | |
| Write | Organization | |
| **Assign** | **Organization** | Already present (task 030 used it) |
| Share | none | Not needed: the BFF application user writes the shares |

Organization depth reaches Secure-team-owned rows, so the writer creates, re-evaluates (`sprk_lastevaluated`) and
closes them with no change.

> **Dependency.** D-29 (task 008) adds AppendTo on the Do-lane subjects. It must be **Organization** depth, not Deep.
> Once the writer sits in a customer child business unit (#1094), Deep would not reach Secure-team-owned subjects in the
> sibling `Secure Record` unit. The same applies to the writer's Read and AppendTo on `sprk_matter`, which today works
> only through the root team's over-grant (security-roles §9.2).

### 4.3 Decision Records: append-only and a secure child at once

**Yes, a Decision Record can be a secure child.** The mechanism never needs Write on the row by a human or by the
writer:

- **Assign.** The owner is set at create. The BFF application user (sysadmin) does any later re-own.
- **Shares.** The mirrored shares may carry Write/Delete, but a share does nothing unless the user's role holds the
  privilege at some depth. No human role holds `prvWrite`/`prvDeletesprk_DecisionRecord` (security-roles §3), so those
  rights are inert. Append-only by privilege still holds.

**Gap 1: the writer lacks Assign.** `Spaarke Ontology Service` holds Create + Read on `sprk_decisionrecord` but **no
Assign** (live). Creating a row owned by the Secure team needs `prvAssignsprk_DecisionRecord` (Organization). That
privilege lets the writer change a ledger row's **owner** later. It does not let anyone change the row's **content**;
Write stays None. This is a privilege-surface change on the append-only principal and needs explicit owner approval.

> **Alternative without Assign.** The writer creates the Decision Record as it does today, and the 2-minute
> recent-changes pass moves it into isolation (that pass writes as sysadmin). Cost: a window of ≤ 2 minutes during
> which the row sits in the writer's business unit.

**Gap 2: a Decision Record with no matter cannot be mirrored.** `sprk_matter` is the Decision Record's only lineage
lookup. A Decision Record with **no matter** that the Secure team owns would be held with every share revoked, so nobody
could read it. See §4.5.

### 4.4 Keeping grants in sync when matter access changes

Nothing new to build. Once the tables are in the lineage map:

| Event | What happens to existing Signal and Decision Record rows |
|---|---|
| `/share-user` or `/unshare-user` on the matter | Re-synced inside the request; the scoped walk follows the new lineage entries |
| Model-driven-app Share/Unshare of the matter | Re-synced by `SecureChildShareReconciliationJob` within 2 minutes |
| A new Signal or Decision Record | Owned by the Secure team at create; its shares arrive within 2 minutes |
| No Access added | `NoAccessShareEnforcer` → `SyncRootAsync` removes the user from these rows at once |
| Matter made Secure after Signals exist | `SecureChildReconciler` (provisioning trigger) re-owns them into isolation, then mirrors the shares. The recent-changes pass also catches stragglers |
| Matter unsecured | The reconciler moves them **out** to the matter business unit's **default team**. ⚠ **This fails today** (see below) |

**Why unsecure fails today, and the fix.** Default teams hold no role with Read on `sprk_signal` or
`sprk_decisionrecord`. Live: the BU1 default team holds `Spaarke Basic User`, `Office Add In User`, `AI Analysis User`
and `Reporting Viewer`. The assign would hit F3. The row would be put back on the Secure team, and the unsecure would
report `children_incomplete` on every retry.

The fix is a role edit, not a uac code change: add `prvReadsprk_Signal` and `prvReadsprk_DecisionRecord` at **Basic**
to `Spaarke Basic User`, the role the default teams hold. After the release, the Signal is team-owned in the matter's
business unit, so `EnsureOwningBusinessUnitMatches` still passes.

### 4.5 A To Do with no matter (O-5 / D-31)

**The resolver handles this better than today's plan does.** With the To Do passed as a parent:

- **Ordinary To Do.** The answer is "not secure", so the Signal keeps the D-31 path (`owningbusinessunit` = the To Do
  owner's business unit).
- **To Do under a secure project or work assignment.** The To Do is Secure-team-owned, so the Signal goes to the Secure
  team. It is mirrored through `sprk_regardingtodo` → `sprk_todo` → the secure root.
- **Today's D-31 plan for that case** would put the Signal in the `Secure Record` business unit owned by the writer,
  never mirrored.

**The Decision Record is the gap** (§4.3 Gap 2). With no matter it has no lineage. Two options:

- **(a) Narrow skip.** R1 skips only "no matter AND filed under a secure root" subjects, and counts them. This is
  recommended: rare, cheap and honest.
- **(b) Schema change.** Add a filing lookup to `sprk_decisionrecord` (for example a regarding project, work assignment
  or To Do, through task 007), plus lineage entries.

### 4.6 Does the reading user see the Signal?

Yes, through the share, regardless of role depth:

- **Who has access.** A user shared on the Secure matter receives a mirrored share on each Signal and Decision Record.
  The user holds `Spaarke Console User` Read on both tables (Parent: Child BU), which is enough for the share to take
  effect.
- **Who does not.** Users not shared on the matter cannot reach the rows by depth in production, because `Secure Record`
  is a sibling business unit. Root-BU users in dev still can, the accepted #1081 artifact.
- **Mirrored Write.** A Collaborate share mirrors Write, so a sharee could resolve a Signal directly until task 049
  removes Console User Write. That is identical to ordinary matters.
- **The BFF read route (task 038 / FR-54).** It filters by "the caller can read the matter", which gives the same
  answer: a sharee passes and a non-sharee is dropped. The Do-lane "my own work" rule is unaffected.

## 5. Effort, risks, dependencies, task fit

### 5.1 Estimate: Medium, about 3 dev-days

| Piece | Estimate |
|---|---|
| Registration: two config entries with live refusal probes, two lineage entries, uac test suites green (the synchronizer's in-memory `SecureChildShareWorld` may need entity sets for the two tables) | 0.5 d |
| `SignalWriter` resolver path, refusal reason, tests | 0.75 d |
| Decision Record writer (task 040, not yet built) takes the owner from the resolver at the start | +0.25 d |
| Role edits and union re-verify: `Secure Record Owner` +2 Read (by script); `Ontology Service` +Assign on Decision Record; `Spaarke Basic User` +2 Read Basic | 0.25–0.5 d |
| Task 031: replace the FR-14a Restricted/Limited skip with "resolver refusal → skip and count", plus the narrow no-matter skip from §4.5 | −0.1 d (it gets simpler) |
| Live gate in dev, following the uac G149-2 recipe (spaarkedev1 has no Secure matter, so one must be created): provision a Secure matter → a Signal is created Secure-team-owned → share to A → A sees it within 2 minutes and B does not → unshare → unsecure releases it to the default team → delete the probes | 0.75–1 d |
| **Total** | **≈ 2.5–3.5 d** |

### 5.2 Risks

1. **Cross-project edits.** `SecureChildLineage.cs` and `config/secure-record-owner-role.json` belong to uac-r2, which
   is still active (task 171 merged today). Its owner must review the change. The config's `howToExtend` demands a
   recorded live refusal for each table.
2. **Deploy order.** The live role edit (`-Apply`) must land in each environment **before** a BFF carrying the new
   config. Otherwise:
   - NFR-05 census clause 5 goes red;
   - every Secure-matter Signal create returns 403 (`0x80040299`), and the writer refuses.
3. **Unsecure release** needs the `Spaarke Basic User` read edit (§4.4). Without it, unsecuring a matter that has
   Signals stalls, reporting `children_incomplete`.
4. **Load on the 2-minute recent-changes pass.**
   - The nightly evaluator stamps `sprk_lastevaluated` on every open Signal, so each night every Signal becomes a
     "changed row". The next pass lists all of them and walks each one upward. Parent reads are cached per run, so
     this should be modest.
   - The listing cap is 20 pages × 5,000 = 100k rows per table per window; above that the run fails.
   - Measure it in dev. If needed, uac could filter the listing, which would be a uac code change, so flag it early.
5. **Environment dependencies.** The writer's Organization-depth Read and AppendTo on secure subjects and matters
   (#1094) and D-29's depth must be right, or creates on Secure matters fail once app users leave the root business
   unit.
6. **uac batch 4 must be deployed** with its jobs running in the target environment. Without the synchronizer and
   reconciler, a Secure-team-owned Signal is readable by nobody.
7. **Decision Records with no matter** (§4.5): narrow skip or a schema change.
8. **Assign on the append-only principal** (§4.3): needs explicit owner sign-off, or use the ≤ 2-minute alternative.

### 5.3 Does uac-r2 have to change its code?

**No logic change.** It needs two data entries in its files and review of them. Two things become uac follow-ups only
if the owner wants them: the census `ChildTables` addition with `sprk_createdbyperson`, and a recent-changes filter if
the load measurement in §5.2 item 4 says so. A uac logic change would be needed only if the owner rejects the
`Spaarke Basic User` read edit, because the resolver would then need a per-table release rule. Avoid that.

### 5.4 Task fit

**This does not fit inside 037 + 008.**

- Task 037 is 4–5 h for the Do-lane subjects. This adds a second ownership path, cross-project registration and a live
  gate.
- Task 008's role edits are owner-pinned to an exact list. Three new edits need fresh approval.

**Proposed: a new task (for example `039-secure-signals-and-decision-records`, FULL rigour, opus), with these
amendments:**

| Task | Amendment |
|---|---|
| 008 | Add the three privilege edits once approved |
| 031 | Swap the skip as described in §5.1 |
| 037 | Pass the subject as a resolver parent |
| 040 | Decision Record owner from the resolver |
| 079 | Becomes "request uac review of the registration PR" instead of "file a deferred issue" |
| Spec | Amend FR-14a, D-15, O-17 and O-20 |

Dependencies of the new task: 008, 030 and 037, plus uac batch 4 deployed in dev.

## 6. Recommendation: do it in R1

Plain-language reasoning for the owner:

- **The skip protects the wrong matters.** "Restricted" and "Limited" only keep **outside** contacts away. Your own
  staff can already open those matters, so hiding their Work Items protects nothing. The matters that really are
  walled off from staff are the **Secure** ones, and the current plan does not skip them.
- **What goes wrong with Secure matters today.** A Secure matter's Work Items would land where the people working that
  matter **cannot see them**. In dev, people who should not see them can.
- **The fix is mostly done already.** The access-control project has built and tested the machinery that keeps every
  document, To Do, event and email on a Secure matter visible to exactly the people the matter is shared with. Plugging
  Signals and Decision Records into it is mostly registration: two list entries each, a small writer change like the
  one already made for spend signals, three role edits, and one live check. About three days.
- **Afterwards, no special rule is needed.** Work Items on Secure matters follow the matter automatically when it is
  shared, unshared, secured or unsecured.
- **Conditions.**
  - The access-control owner agrees to the two list entries.
  - You approve the three role edits.
  - That project's batch 4 is deployed in dev.
- **Fallback if any condition fails.** Keep a skip, but key it on the **Secure** flag rather than on Restricted or
  Limited. That at least protects the right matters.
