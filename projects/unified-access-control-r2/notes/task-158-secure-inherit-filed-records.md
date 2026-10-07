# Task 158 — a work assignment or project filed under a SECURE matter or project is itself secure (owner round 6)

> Branch `task/uac-r2-158` = `task/uac-r2-148-r2` + `task/uac-r2-156-c1-r2` (merged, conflicts resolved preserving both
> intents) + `work/unified-access-control-r2`, base **`c8a964cb69a82f9a2e697d12535f097b9227055f`**.
> **Fix round r1** (§14): `task/uac-r2-158-r1` = 158 + `task/uac-r2-142-r6` (merge `5d041a627`, the `sprk_assignedaccess`
> ledger round 30 extends) + `a58cbd8ee`; stopped by a machine restart (WIP `c5decdbeb`); completed on
> **`task/uac-r2-158-r1c`** (base `c5decdbeb`). **Fix round r1c-v1** (§15): `task/uac-r2-158-r1c-v1` from
> `task/uac-r2-158-r1c` (`25b7ac3dd`) — the verification's 17 items. **Fix round r1c-v2** (§16):
> `task/uac-r2-158-r1c-v2` from `task/uac-r2-158-r1c-v1` (`f090b1221`) — main-session rounds 39 and 47 (the re-verification).
> **Final fix round** (§17, owner round 56's cap): `task/uac-r2-158-h` from `task/uac-r2-158-r1c-v2` (`e99a329b2`) —
> main-session round 58 (a later No Access entry reaches the filed records; the failed-revoke marker put back; the
> by-parent ledger read rows-in-force only; the already-ordinary copy). Known limits: §17.7.
> Status: **code complete, not deployed.** Live steps are manual gates for the main session (§10).
> Binding owner decisions: round 6 items 1, 2, 4 and the item-4 clarification; round 3b / round 10 item 7 (F3, via task
> 146-c1's `SecureDesignationRemoval`); round 7 item 3 (G5 chat create); round 8 item 1 + round 13 item 7 (§6.5 path B
> for app-only follow-ons of the user-OBO tools); round 17 item 3 (an EMPTY `sprk_issecure` is never "not secure");
> S5 (round 3: a secure record always has at least one person who can see it); R3/R4 (minutes, never hourly);
> **round 30** (inherited-share provenance on task 142's ledger, option (c)); **round 31** (the creator honours the
> record's AND every secure parent's No Access list before any write; a record created under a secure parent is created
> INTO isolation); **owner round 32** (§6.5 path B, secure inline — ACCEPTED); **round 39** (unsecuring a parent ends
> what it passed on — interpretation xiii REVERSED; DIRECT shares on a filed secure record honour every secure parent's
> No Access list); **round 47** (E-158-v1-1's complete fix; the Declined marker before the revoke; the surviving seeds
> pinned; the Office copy); **owner round 56** (fix runtime / maintainability / performance defects, record the rest as
> known limits, no new machinery, one more round at most); **round 58** (this task's final round).

## 1. Outcome

A `sprk_workassignment` or `sprk_project` **filed under** a `sprk_matter` or `sprk_project` whose `sprk_issecure = true`
becomes a real secure root — `sprk_issecure`, owned by the named `Secure Record Owners` team, its own SPE container, the
person who created it shared — through **`ProvisionProjectEndpoint`'s own steps** (no second "make secure"), and the
secure parent's sharees are mirrored onto it with task 149's rule. From then on every existing secure rule applies to it
with no new code: FR-22 direct-only for contacts (reads the record's own flag; pinned for a secure work-assignment root
by `UnifiedEvaluatorSeamTests` criterion 4), the No Access list, the isolation census, and task 155's own-container
resolution (supersedes 155's interpretation (iii) for these records).

| Rule | Implementation |
|---|---|
| "Filed under" | a work assignment's typed `sprk_regardingmatter` / `sprk_regardingproject`; the polymorphic pair (`sprk_regardingrecordid` text + `sprk_regardingrecordtype` → `sprk_recordtype_ref.sprk_recordlogicalname`) on a work assignment or a project (a project has no typed regarding lookup — live metadata, re-checked 2026-10-04). The pair's **type decides**: a pair naming a matter's id with an invoice type is not filed under the matter |
| Parents | matter or project only (a pair naming an event, invoice, work assignment … is not this rule — interpretation ii) |
| Secure-if-any | one readably secure parent is enough. An unreadable / EMPTY parent flag or an unresolvable pair is never "not secure": with no other secure parent → **unverifiable** (nothing written; a BFF write refused); beside a readably secure parent → the record would be secure, but the person it is secured for cannot be checked against the unreadable parent's No Access list (round 31 item 1), so provisioning / a create / a re-file **refuse** (`sdap.provision.creator_no_access_unverifiable`) and the job reports it (interpretation iii, revised in r1) |
| The person it is secured for | its recorded creator (provisioning: `createdby` when a usable person, else `sprk_createdbyperson`; a create: the caller) — checked against the No Access list of the record AND of every secure parent BEFORE any write, never dependent on the flag (round 31 item 1, §14) |
| Created under a secure parent | created **INTO isolation**: by the application, owned by the named Secure Record Owners team, `sprk_issecure` in the create, `sprk_createdbyperson` = the caller, after the as-caller G5 pre-check; then its creator shared and read back (else the row is deleted again and the create refused), its container, its sharees (round 31 item 2) |
| Never auto-unsecure | nothing in this task takes a record out of isolation; re-filing it elsewhere or unsecuring its parent leaves it secure (round 6 item 4) — but unsecuring a parent ENDS the unmodified shares it passed on (round 39 item 1, r1c-v2, §16) |
| Sharees | task 149's mirror (rights ∩ Read/Write/Append/AppendTo/Delete, never Share/Assign; INTERSECTION over several secure parents; the No Access guard; a flagged-not-isolated parent holds), each share's **provenance on task 142's `sprk_assignedaccess` ledger** (round 30): a secure parent's unshare removes the inherited share only where it came from that parent and is still unmodified (§4) |

## 2. Inventory (step 1)

### 2a. BFF paths that create or re-file a work assignment or project with a filing link

| # | Path | Create / re-file | Wired |
|---|---|---|---|
| W1 | `dataverse.create_record` (`DataverseCreateRecordHandler` → `OwnedChildWrite`) | create (any table) | r1: G5 (incl. AppendTo on a pair-named secure parent) → `PlanCreateAsync` (refused / ordinary / INTO isolation) → isolated app create (named team + flag in the create) → `CompleteIsolatedCreateAsync`; a removed row = refused, a stranded row = named, incomplete = ERROR naming the record. Driven end to end through `POST /api/ai/chat/sessions/{id}/gates/{gateId}/resolve` (`SecureRootCreateRouteTests`) |
| W2 | `dataverse.update_record` (`DataverseUpdateRecordHandler`) | re-file | pre-check (r1: incl. the recorded creator's walls on the record and every secure parent) + secure after the caller's PATCH; incomplete → error |
| W3 | Playbook output orchestrator `DataverseUpdateHandler` | re-file | pre-check (throws `RecordOwnerUnresolvedException`) + secure after; incomplete logged → job |
| W4 | UpdateRecord node / ActionSeam `UpdateRecordActionCore` | re-file | pre-check (scope-resolved gate; no gate in the host → refuse a filing write) + secure after |
| W5 | Field-mapping push `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` | re-file (bulk) | pre-check per record (refusal = that record failed) + secure after; no gate → refuse a filing write |
| W6 | Office quick-create `RecordCreationService.CreateProjectAsync` (pair via the Field Mapping Framework) | create | r1: `PlanCreateAsync` with the OBO G5 (Create on `sprk_project`, AppendTo on each secure parent) → INTO isolation (named team + flag in the create; a mapping rule may not write `sprk_issecure`) → `CompleteIsolatedCreateAsync`; removed = failed (500), stranded / incomplete = warning. Driven end to end through `POST /api/office/quickcreate/project` (`SecureRootCreateRouteTests`) |
| — | `POST /api/v1/work-assignments` | create | **not wired**: task 166 deletes it (owner round 10 item 1; 166 note §13 "Note to task 158"). It cannot file under a matter anyway — it writes `sprk_matterid`, which the table does not have (the pre-existing bug the POML names is resolved by the deletion). The census waiver for it stays with 166 |
| — | Wizards, MDA forms, imports, flows (`Xrm.WebApi`, outside the BFF) | both | the job (≤ 5 min) |
| — | External SPA, `IncomingAssociationResolver`, `TaskActionCore`, Office save | — | write documents / events / to-dos / communications only — no work assignment or project (checked: `ExternalDataService` writes `sprk_documents`, `sprk_events`, `sprk_todos`) |

### 2b. Live, read-only (spaarkedev1, 2026-10-04, Dataverse MCP)

- Secure matters: **0**. Secure projects: **1** — `65a3fab2-77a5-f111-aaad-70a8a590c51c` ("Test New Matter via Workspace"),
  owned by team `6eabc7f9-13be-f111-a05b-0022482913fc`, container `b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`.
- Work assignments filed under it: **0** by `sprk_regardingproject`, **0** by the pair. Projects filed under it by the
  pair: **0**. Work assignments in the environment: 22.
- `sprk_recordtype_ref` has 14 rows (`sprk_matter` = `e8547bb4-8600-f111-8407-7c1e520aa4df`, `sprk_project` =
  `ca68b3bb-8600-f111-8407-7c1e520aa4df`).
- **So the job's first run after deploy secures nothing in dev** (0 candidates).

## 3. The four triggers — one decision, provisioning's own steps

`Services/Access/SecureRootInheritance.cs` (scoped) owns the decision; it never writes a step itself:

- **`SecureIfFiledUnderSecureAsync(table, id)`** — read the row's filing (app-only, `IGenericEntityService`), decide its
  secure parents; not secure → nothing; isolated already → only the sharees; otherwise
  **`ProvisionProjectEndpoint.ProvisionInheritedAsync`** (the endpoint's core with no caller: `ProvisioningCreator.RecordedCreator`
  — `createdby` when a usable person, else the BFF-stamped `sprk_createdbyperson`, task 133's resume rule; no colleagues;
  the record may arrive unflagged and Step 4.1 — task 150's `EnsureSecureFlagAsync`, copied verbatim — flags it first,
  read back; every other step, refusal and compensation is the endpoint's). Then `SecureChildShareSynchronizer.SyncInheritedRootAsync`.
- **(1) create** (r1, owner round 31 item 2) — `SecureRootFilingGate.PlanCreateAsync` BEFORE any write: the filing as the
  create will leave it; no secure parent → the writer's ordinary create; an unreadable parent with no secure one →
  `record_owner_parent_undetermined`; otherwise, in this order: the writer's as-caller G5 on the secure parents (asked
  before anything else is said about them), a secure+unreadable filing → `creator_no_access_unverifiable`, the caller
  against every secure parent's No Access list and the record's own (`CheckProspectiveAsync`: the organizations the
  payload references), the named team resolved → **isolated** plan. The writer creates the row owned by that team with
  `sprk_issecure = true` and `sprk_createdbyperson` = the caller in the create, then `CompleteIsolatedCreateAsync`
  (provisioning's re-entry branch for its recorded creator: share read back → container → sharees). A creator share
  that is not in place at the end → the row is DELETED (read back gone) and the create refused; a row that can neither
  be shared nor deleted is reported as such (`RowStranded`) and the job shares it.
- **(2) re-file** — `SecureRootFilingGate` (singleton; scope factory; registered beside the restamper by
  `AddCoreAncestorResolver`): `CheckAsync` BEFORE the write (reads the row as the write will leave it — the writers'
  spellings: logical name or `Nav@odata.bind`; `EntityReference`, `Guid`, bind path, GUID text, JSON string, null —
  and refuses `record_owner_parent_undetermined` when no parent is readably secure and one cannot be read, or a value
  cannot be interpreted; r1: when the row would be filed under a secure parent, its recorded creator is checked against
  the No Access list of the record and of every secure parent, and a creator who is walled, cannot be checked or cannot
  be named refuses — nothing written), `SecureAfterWriteAsync` AFTER it with the write's own columns (a write that files
  nothing costs no read; never throws; a failure is logged and left to the job).
- **(3) a parent becomes secure** — `ProvisionProjectEndpoint` Step 8 (task 148's `ChildrenFollowAsync`): after the
  record's children, `SecureFiledRootsUnderAsync` secures every work assignment / project filed under it (each through
  `ProvisionInheritedAsync`, whose own Step 8 secures what is filed under IT — the tree, bounded by
  `MaxNestedProvisioning = 3`, deeper records left to the job, `sdap.inherit.too_deep`). Incomplete → the existing
  500 **`sdap.provision.children_incomplete`** with `filedRecordsSecured` / `filedRecordsRemaining` / `filedRecords`
  (the client already classifies the code — interpretation vi); the 200 carries `filedRecords`. The already-provisioned
  branch runs the pass too (200 `childrenOnly` when it secured something; 409 `already_provisioned` otherwise).
- **(4) writes outside the BFF** — `SecureRootInheritanceJob` (ADR-036 `IScheduledJob`, `*/5 * * * *`, writes on,
  `AddScheduledJob` in `ExternalAccessModule`): lists every matter/project flagged secure AND every one whose flag is
  EMPTY (r1, round 17 item 3; paged; past 20 pages → throw), the records filed under them (one page per query; more →
  throw; the pair found in every spelling `Guid.TryParse` accepts — r1), secures each (not-yet-secure ones in a fixed
  order from a cursor, at most `MaxProvisioningsPerRun = 25` provisionings per run — r1: any deferral makes the run
  `Success = false` and the next run continues after the last record reached), gives every secure one its parents'
  sharees and reconciles their provenance, then (r1) reconciles the provenance of records re-filed AWAY from a secure
  parent; a failed scan is a failed run (throws); any incomplete record → `Success = false` naming it; heartbeat per
  attempt. **POML escalation trigger does not fire**: the job reads DATA, not the path that wrote it, so no writer
  outside the BFF can hide a filed record from it for more than one run.

## 4. Sharees — `SecureChildShareSynchronizer.SyncInheritedRootAsync`

Task 149's mechanism applied to a ROOT filed under secure parents: the parents' direct shares (strict read), the
intersection over isolated parents, `ChildMirrorMask`, the No Access guard on the record and every parent (walled →
dropped; unverifiable → held), a flagged-but-not-isolated parent → held (`Incomplete`), then GRANT a missing principal or
MODIFY to `current | mirror`; read back through the strict read; then 149's `RunAsync(record)` for the record's own
children when anything was written. The synchronizer itself never revokes or narrows a root.

**Provenance (r1, owner round 30, option (c)) — `SecureRootInheritance.GiveParentShareesAsync`**, around the mirror, on
task 142's `sprk_assignedaccess` ledger (no new table): one row per (filed record, parent, principal) — source field
`inherited:{parentTable}:{parentId}`, subject `sprk_subjectsystemuser` or the new `sprk_subjectteam`, the mask written in
`sprk_grantedlevel` (a raise records the level it raised from as `raised-from-mask:N`); a principal that already held the
mirror is `CoveredByExisting` (direct access). Before the mirror: (a) an inherited share removed or narrowed on the filed
record outside the BFF → `Declined` (`/unshare-user` on the filed record marks it `Declined` through 142's marker) —
**unless (r1c-v1) the person is on the filed record's No Access list**: then task 143's enforcer removed it, a known
cause, so the row waits (`Skipped`, `removed-by-no-access`) and the share is passed on again once the wall is lifted
(task 142's criterion 9); an unverifiable list decides nothing (nothing re-added, nothing recorded, reported);
(b) a row whose parent is isolated and no longer shares the principal is ended by the reverse rule — a `Declined` row ends
too ("never re-added WHILE the parent share persists"), and is taken out of the declined set at once, so a principal a
NEW parent passes on (the record was re-filed) is given in the same pass; a parent that cannot be read is reported.
(c) the add-only mirror, never re-adding a declined principal, **with write-ahead provenance (r1c-v1, verifier item 1)**:
each share's row is written BEFORE the share — `Shared`, reason `share-pending` (plus `;raised-from-mask:N`), the mask
about to be written — and a row that cannot be written (a fault, or a concurrent pass created it first: the store answers
`null` and writes nothing over it) writes NO share. (d) each share written and read back has its row confirmed (the
marker dropped); a principal that already held the mirror is recorded — inherited when a live row of another parent says
so, `CoveredByExisting` only when NO live row says the share was passed on (the rows are re-read after the shares were
read, so a concurrent pass's row for any share this pass saw is in that read). A row whose share never landed is written
again by the next pass, never read as a removal. An unreadable provenance gives nobody anything; an unwritable one writes
nothing and is reported.

**The reverse rule** (`EndInheritedSourceAsync`, A4's rules): `Declined` ends (the operator's removal stands);
`Adopted` / `CoveredByExisting` are direct → kept; a `Shared` share is removed ONLY when (1) the record's current secure
parents' intersection no longer carries it — a read parent that does not carry it, or none readable, ends it (task 149:
"a principal its known roots do not share is revoked"); every read parent carrying it while another cannot be trusted
holds it (reported) — (2) no independent Assigned-To row names the user, (3) it is still the unmodified mask written,
(4) it is not the record's last reader (S5); then put back to the level it raised from, or revoked, read back, and the
record's own children brought into line. Triggers: `/unshare-user` on a SECURE parent (`PassUnshareOnAsync`, from the
ledger rows sourced from it — failures = `children_incomplete`); the job, (b) above for the records it lists and
`ReconcileUnvisitedProvenanceAsync` for records re-filed away from the parent; `/unsecure-project` of the filed record
itself ends every row on it before its shares are revoked (Step 3.6).
r1c-v1: (1) compares masks — a parent carrying the person at a LOWER level than the share does not justify it
(`AccessRemoved`, never `KeptOtherSource`), and when the record's current parents still pass part of it on, the
`/unshare-user` fan-out gives that part back in the same call (the mirror's own pass; reported as `filedRecordsNotUpdated`
when it cannot be given); a row recorded ahead of a share that never landed ends with nothing removed
(`assignment-ended`); the record's LAST reader is kept with its row LIVE (`Shared`, reason `kept-last-reader`) — the share
is still the one the parent passed on, so every later pass tries again and removes it once someone else can open the
record (an ended row would later have been read as direct access).
r1c-v2 (§16): (2) counts an Assigned-To row only when task 142 WROTE (`Shared`) or an operator ADOPTED (`Adopted`) the
access — never `CoveredByExisting`, 142's observation of a share that may be this rule's (round 47 item 1 (1)); every such
covered row is set `Skipped` / `covering-share-ended` BEFORE the share is removed or narrowed and 142's materializer runs at
once (round 47 item 1 (2)). New trigger: the UNSECURE of a parent (`EndWhatAParentPassedOnAsync`, Step 4.5 — round 39
item 1: the parent justifies nothing; §5). A parent that reads NOT secure, or no longer exists, passes nothing on wherever
the rule looks (step (b), `PassUnshareOnAsync`); one flagged secure but not isolated holds; an EMPTY flag is unreadable
(reported). And (e), the post-write check: each share a pass WROTE is decided again once it has landed — a row a concurrent
reverse pass ended is put back on record when the share is in place, and a share whose parent no longer passes it on
(unsecured, re-owned out of isolation, unshared meanwhile) is ended by this rule at once (§16.1).

## 5. The way back — `/unsecure-project` (owner round 6 item 4 + clarification)

- Unsecuring a matter/project leaves its secure filed records secure, and the response LISTS them —
  `relatedSecureRecords: [{recordType, recordId, name}]` (only records PROVABLY filed under it: typed, or a pair whose
  type was read; `relatedSecureRecordsUnreadable: true` when the list could not be read).
- `alsoUnsecure: [{recordType, recordId}]` (optional, validated before anything is written — 400 for a bad entry):
  after the record itself is unsecured, each is checked against **F3 on that record** — `SecureDesignationRemoval`
  (task 146-c1's ONE helper: Full Access = Write + Delete on it, or its creator by `createdby` then `sprk_createdbyperson`)
  — and unsecured through the endpoint's own steps, or reported (`refused` with `sdap.unsecure.not_permitted` /
  `sdap.unsecure.permission_unverifiable`; `sdap.unsecure.not_related`; or the code its own unsecure answered) in
  `relatedRecordsUnsecured`. Nothing not asked for is unsecured.
- **Step 3.6 (r1, round 30):** before the record's shares are revoked (Step 4), its inherited-share rows end
  (`record-unsecured`); not done → 500 `sdap.unsecure.children_incomplete` (`inheritedSharesNotEnded`), flag and shares
  stay, the same call completes it. Without it a later re-secure read each revoked inherited share as an operator's
  removal and withheld the parents' sharees for good.
- **Step 4.5 (r1c-v2, main-session round 39 item 1 — interpretation xiii reversed):** after Step 4 revokes the matter's /
  project's own shares and BEFORE its flag is cleared, `SecureRootInheritance.EndWhatAParentPassedOnAsync` runs round 30's
  reverse rule on every live row the record passed on (from its own provenance, so a repeat call ends exactly what is
  left), the record justifying nothing though its flag still reads secure. Ended: the unmodified inherited share. Kept: a
  direct share, a raised mask (put back), a share another secure parent still justifies, the record's last reader (S5). Not
  complete (a removal that failed, an unreadable or truncated provenance) → 500 `sdap.unsecure.children_incomplete` with
  `filedRecordsNotUpdated`, the flag kept, the same call completes it.
- **Step 5.5 (r1c-v2):** after the flag is cleared, what the filed records' OTHER secure parents still pass on is given back
  (`GiveBackAsync` — the mirror's own pass; a flagged-but-not-isolated parent holds every mirror, so not before Step 5); not
  given back yet → 500 `children_incomplete` (`filedRecordsNotUpdated`), the record unsecured, the job gives it.
- **Already ordinary (r1c-v2):** the repeat call on a matter / project that is not secure ends what it is still on record as
  passing on (a flag cleared outside the BFF, or a pass that wrote while an earlier unsecure ran) and gives back the rest —
  `children_incomplete` when it cannot.
- **Interpretation (owner-reversible, recorded in the POML):** a record whose parent is STILL secure cannot be unsecured —
  409 `sdap.unsecure.parent_still_secure` naming the parent (`parentRecordType`, `parentRecordId`, `parentName`), nothing
  written; an unreadable parent → 500 `sdap.unsecure.parent_unverifiable`, nothing written. Once the parent is not
  secure, the same call unsecures it.
- **UI** (the POML's checkbox list): no client unsecure surface exists on this base or on `integ/uac-r2-batch4` (task
  150's ribbon step stopped on 142). The BFF contract above is complete; the ribbon flow (task 150, in 142's Access
  group per round 26) renders `relatedSecureRecords` as checkboxes and posts the ticked ones in `alsoUnsecure` —
  handover §12.

## 6. Interpretations and decisions

| # | Decision | Why / alternative |
|---|---|---|
| i | **SUPERSEDED by round 31 item 2 (r1).** Was: created as an ordinary row of the caller's unit, then provisioned forward. Now: created **INTO isolation** (named team + flag in the create, `sprk_createdbyperson` = the caller) after the G5 and No Access checks; creator share read back, else the row is deleted and the create refused | Round 28 E1 / round 31: no business-unit-visible window. S5 still holds: a row its creator cannot open is deleted (read back), and one that cannot be deleted either is named and shared by the job (≤ 5 min) |
| ii | Parents are matters and projects only | the round-6 question as asked; a pair naming an intermediate (event, invoice) is task 155/156's domain |
| iii | Secure-if-any with an unreadable sibling: **refused** (r1 — round 31 item 1: the creator cannot be checked against the unreadable parent's No Access list), reported by the job; was: secure, sharees held | securing for a person who may be walled off the unreadable parent would breach N6; the record stays as it was until the parent can be read |
| iv | **SUPERSEDED by round 30 (r1).** Was: add-only, a parent's unshare not propagated. Now: provenance on the 142 ledger; a secure parent's unshare removes only the unmodified share it passed on (§4) | Round 30 option (c) |
| v | `POST /api/v1/work-assignments` not modified | task 166 deletes it (§2a) |
| vi | Incomplete filed records reuse `sdap.provision.children_incomplete` | the client already classifies it; a new code would be an unrecognised 500 in the wizard |
| vii | F3 is task 146-c1's helper, byte-identical (blob `13ba8f80b`, identical on `integ/uac-r2-batch4`) | "exactly one F3 check"; task 150's unsecure F3 gate is not on this base — the main session keeps one (§13) |
| viii | The user-OBO chat tools secure INLINE (app-only provisioning after the caller's own write) | **§6.5 path B — ACCEPTED by owner round 32** (2026-10-04, "Path B: secure inline"); recorded as Amendment A-UAC158 beside A-UAC156 (round 8 item 1) / round 13 item 7 in spaarke-ai-architecture-redesign-r1's spec; `DataverseUpdateRecordHandler` / `DataverseCreateRecordHandler` remarks say ACCEPTED |
| ix | Nesting bound 3 per call | a pathological project chain cannot make one request unbounded; the job completes deeper levels |
| x | Listing is type-exact; a pair whose type cannot be read is an **unconfirmed candidate**: the transition and the job decide it (→ unverifiable, reported), the unsecure endpoint never shows it as related | the job must not miss a record; the unsecure list must never offer an unrelated record |
| xi | A record filed under a parent flagged secure but not isolated is secured (the flag is what it inherits); no sharee is mirrored from that parent until it is isolated | 149's rule for children, applied to a root |
| xii | A parent's later sharee reaches its SECURE filed records **inline**: `/share-user` calls `SecureRootInheritance.PassSharesOnAsync` after task 149's child fan-out, and a project that was just GIVEN sharees passes them on to what is filed under it in the same call (the nested-provisioning order: a work assignment secured inside a project's provisioning, before that project had its matter's sharees). A sharee-only pass never provisions (`sdap.inherit.not_yet_secure`: the job secures it); bounded by the same depth; never fails the share (logged; the job completes). A model-driven-app Share of the parent reaches them through the job | owner R3/R4 ("immediate on save; the job only a safety net"); task 149 fans out to children inline the same way. `InternalShareEndpoints.ShareAsync` gains one dependency — a hand-merge point with 142 (§13) |

**r1 interpretations (owner-reversible; each applies a binding round to a case it does not name):**

| # | Decision | Why / alternative |
|---|---|---|
| xiii | **REVERSED by main-session round 39 item 1 (r1c-v2, §16).** Was: an unsecured parent ends nothing it passed on. Now: **unsecuring a parent ENDS what it passed on** — `/unsecure-project` Step 4.5 runs round 30's reverse rule on every row the parent passed on (only the unmodified inherited share ends; a direct share, a raised mask, a share another secure parent still justifies and the record's last reader are kept), failures report through `children_incomplete`; and a parent that reads NOT secure (or is gone) passes nothing on wherever the reverse rule looks (the job's step (b), `/unshare-user`, a repeat unsecure) | Round 39: after the unsecure the filed records are still secure, and a sharee's access to them came only from the parent share just revoked — round 30's "a former sharee with access they should have lost". Seeing the now-ordinary parent through its business unit gives no access to a still-secure filed record, so the earlier reading's exception does not apply. Owner round 6 item 4 still holds for the RECORDS: they stay secure (never auto-unsecure) |
| xiv | A record **re-filed away** from a secure parent keeps what it passed on until that parent's unshare or unsecure (BFF or out of band) | Re-filing is not an unshare; never lower existing access (A4). The job reaches it through the parent's own ledger rows |
| xv | The reverse rule decides "still justified by another parent" on the **intersection** with task 149's rule for untrusted parents: a read parent that no longer carries the person ends it; every read parent carrying it while another cannot be trusted holds it (reported) | Owner round 11 item 4 (intersection) + 149's "a principal its known roots do not share is revoked; held children are only narrowed". The first round kept everything when any parent could not be trusted (fail-open on removal) |
| xvi | The unsecure of a filed record ends its inherited rows FIRST (Step 3.6) and stops (children_incomplete, retryable) if it cannot | A row left `Shared` after Step 4's revoke reads, at a later re-secure, as an operator's removal |
| xvii | Inherited rows are left out of task 142's ledger-roots scan (its OData filter — the one statement of the predicate since r1c-v1, pinned by `AssignedAccessStoreODataTests`) | They are not Assigned-To rows (no contact / organization subject — the materializer already ignores them); counting them would truncate and fail 142's job in a large environment |
| xviii | A stranded isolated create (creator not shared AND the row not deletable) is named to the user (chat error / Office warning) | Never "shared to you" for a row only an administrator can open; the job shares it to `sprk_createdbyperson` |
| xix | A share on a matter / project whose flag is EMPTY lists the records filed under it and reports each (unverifiable → `children_incomplete`, `filedRecordsNotUpdated`); a share on an ORDINARY one reads nothing filed under it | Round 17 item 3 (an empty flag is never "not secure"); the first round short-circuited both as "not secure" |

**r1c-v1 interpretations (owner-reversible; each applies round 30 / ADR-003 to a case the rounds do not name):**

| # | Decision | Why / alternative |
|---|---|---|
| xx | **Write-ahead provenance**: an inherited share's row is written (`share-pending`) BEFORE the share; a share is never written without its row | Verifier item 1 (CONFIRMED): share-then-row let a fault or a share/job race leave the rule's share with no row, and the next pass recorded it as DIRECT (`CoveredByExisting`) — the parent's unshare then never removed it. Alternative "never infer direct from AlreadyCovered without a pre-share read" needs the same pre-share record, so it IS write-ahead. The intent is a reason marker on a `Shared` row, not a new choice option: no schema change, and 142's operator markers (`MarkShareRemovedAsync` → Declined, `MarkShareAdoptedAsync` → Adopted) already treat it exactly as the share it is |
| xxi | A create that loses the alternate-key race answers `null` and writes NOTHING over the row (store), and the pass is incomplete (retried) | The loser decided on a read that did not see the row; overwriting it was the second half of verifier item 1's race (a `CoveredByExisting` written over the winner's `Shared`) |
| xxii | The record's last reader (S5) is kept with its row LIVE (`Shared`, `kept-last-reader`), retried every pass | An ended row left the parent's share on the record with no live provenance, so a later re-share recorded it as direct — the same misclassification as item 1 by another path; and "kept as the last reader" is a condition at the time, not a permanent exemption |
| xxiii | A share task 143's enforcer removed (the person on the FILED record's own No Access list) is `Skipped` / `removed-by-no-access`, not `Declined`; passed on again once the wall is lifted | Task 142's criterion 9 for the same ledger (the enforcer's removal is a known cause, restored once lifted); round 30's Declined is "an operator's removal". **EXTENDED by main-session round 58 item 1 (§17):** task 143's enforcer now also removes a person's share on a filed record for a secure PARENT's list, so the wall checked is the record's own list AND every secure parent's (the guard's one entry point, over the filing the pass already read) — and the same in task 142's Shared branch. Was: the record's own list only |
| xxiv | A raise over an operator's own (`Adopted`) share is recorded (write-ahead), the operator's level as the level it raised from | Before r1c-v1 the raise went unrecorded and `KeptAdopted` kept it for good after the parent's unshare (over-retention); now the unshare takes back only the raise (A4: never lower what was there before) |
| xxv | When the parent's unshare removes a share the record's CURRENT parents still pass on in part, that part is given back in the same call (`PassUnshareOnAsync` re-runs the mirror's own pass for that record), reported when it cannot be | Verifier item 3: the alternative — narrowing in place — would leave the rule's narrowed share without live rows (read as direct later); revoke-then-mirror keeps provenance exact, and R3/R4 rule out leaving the person without the remaining parent's share until the job |

## 7. Placement (CLAUDE.md §10) and component justification (§11)

All in `Sprk.Bff.Api` (ADR-052: BFF identity, BFF domain code, low volume; schedule → ADR-036 `IScheduledJob`). No
package, endpoint, column, option class, interface or plugin (ADR-002). Registrations unconditional (ADR-032, §10 F.1).

| New surface | (1) Existing | (2) Extension | (3) Cost of doing nothing |
|---|---|---|---|
| `SecureRootInheritance` (scoped, concrete — ADR-010) | Provisioning secures one named record; `SecureChildReconciler` moves CHILDREN and never a root; `RecordOwnershipResolver` decides owners and refused a root under a secure parent; 155's resolver only reads filing | Not in the endpoint (five writers, the transition and a job ask "is it filed under a secure record"); not in the reconciler (its "never move a root" rule is load-bearing for 148); not in the resolver (no provisioning dependencies) | A work assignment under a secure matter stays readable by its whole BU; its files go to the matter's container while its access follows its BU; a contact granted on it gets what FR-22 forbids on the matter |
| `SecureRootFilingGate` (singleton) | `CoreAncestorAfterWriteRestamp` (156) is the same shape for the stamp | The restamper's contract is a different invariant; reusing it would couple two owners | Without one cheap singleton each writer (singletons, nodes, Office) would need the scoped service and its provisioning graph injected |
| `SecureRootInheritanceJob` (`IScheduledJob`) | 148's reconciliation job sweeps CHILDREN of secure roots, report-only | Its walk never touches a root and is disabled/report-only by owner decision; this rule is writes-on (round 6) | A record filed by a wizard, a form or an import is never secured |
| `SecureRootInheritance.PassSharesOnAsync` (method) + the `/share-user` call | 149's `SyncRootAsync` fan-out for CHILDREN | It IS the extension: the same inheritance pass in a sharee-only mode | A parent's new sharee would wait up to 5 minutes for the filed records (R3/R4: immediate on save) |
| `SecureChildShareSynchronizer.SyncInheritedRootAsync` (method) | `SyncRootAsync` mirrors onto Secure-team-owned CHILDREN | It IS the extension: same strict read, mask, intersection, guard; add-only for a root | The parent's sharees cannot see the secured record (round 6: "the parent's sharees can see it") |
| `ProvisionProjectEndpoint.ProvisionInheritedAsync` + `ProvisioningCreator` (nested) | The endpoint's caller path | A second entry into the SAME core; the creator is the record's recorded creator | A second "make secure" implementation (forbidden by the POML) |
| DTO fields: `ProvisionProjectResponse.FiledRecords` (+ `SecureFiledRecordsSummary`), `UnsecureProjectRequest.AlsoUnsecure` (+ `RelatedRecordRef`), `UnsecureProjectResponse.RelatedSecureRecords` / `RelatedRecordsUnsecured` / `RelatedSecureRecordsUnreadable` | the existing two endpoints' contracts | additive JSON | the owner's "list the related records / unsecure a subset" has no contract |
| Reason codes `sdap.inherit.*` (7), `sdap.unsecure.parent_still_secure` / `parent_unverifiable` / `not_related` | — | — | an unexplained refusal |
| `SecureDesignationRemoval.cs` + `IDataverseRecordShareService.GetPrincipalRightsAsync` default member | copied byte-identical from task 146-c1-r1 / `integ/uac-r2-batch4` | — | F3 on related records would be a second check |

Publish-size measurement skipped (instruction); code only, no package → no new CVE surface.

**r1 additions** (all in `Sprk.Bff.Api` or its test project; no package, endpoint, interface, option class, job
registration or plugin; registrations unchanged — `AssignedAccessStore` was already registered unconditionally in the
same module):

| New surface | (1) Existing | (2) Extension | (3) Cost of doing nothing |
|---|---|---|---|
| Column `sprk_assignedaccess.sprk_subjectteam` (lookup → team; `scripts/Set-AssignedAccessLedgerSchema.ps1` dry run / `-Apply` / `-Verify`, solution-membership helper) + `AssignedAccessLedgerRow.SubjectTeamId` | `sprk_subjectsystemuser` names a USER subject | Round 30 names it ("add a `sprk_subjectteam` lookup"); a team is a different principal type — Dataverse lookups are single-target | A secure parent's TEAM sharee passed on to a filed record has no provenance, so its unshare could never remove it |
| `AssignedAccessStore` inherited-row members (`ReadInheritedLedgerAsync`, `ReadInheritedLedgerByParentAsync`, `CreateInheritedLedgerAsync`, `InheritedSourceField/Of`, `InheritedLedgerKey`, `InheritedPrincipalOf`, `IsAssignedToScanRow`) + reason codes | 142's ledger members read/write Assigned-To rows by contact / organization subject | They ARE the extension round 30 mandates (no new table — §11); same table, key and write path | No provenance store: option (c) cannot be implemented |
| `SecureShareNoAccessGuard.CheckForSecuringAsync` / `CheckProspectiveAsync` (+ `ExternalParticipationService.OrganizationLookupAttributesOf`) | `CheckAsync` | Same core (`CheckCoreAsync`), one scope switch: the flag is not read when the record is BEING secured; a record with no id yet is asked by its payload's organizations | The verifier's CRITICAL: an unflagged inherited record read `NotSecure` and a walled creator was shared |
| `ProvisionProjectEndpoint.CheckCreatorWallsAsync` (+ `CreatorWallDecision`) / `ResolveRecordedCreatorAsync` | Step 4's `noAccessGuard.CheckAsync` on the record only | Replaces it at both call sites (forward, resume) — one function for the record's and every secure parent's list | Round 31 item 1 (parents' lists; never flag-dependent) unmet on the provisioning path |
| `SecureRootInheritance.PlanCreateAsync` / `CompleteIsolatedCreateAsync` (+ `SecureRootCreatePlan`, `SecureRootFilingGate` pass-throughs, `OwnedChildWrite.CreateIntoIsolationAsync`, `Outcome.PlanRefusal/Isolated/IsolatedFor`, `RecordCreationService.CallerMayCreateUnderAsync` + 3 codes, `SecureRootInheritResult.RowRemoved/RowStranded`) | `CheckRefileAsync` (decides before a re-file); provisioning's re-entry branch | The plan reuses the same decision (`DecideParentsAsync`) and completion reuses provisioning's re-entry branch — no second "make secure" | Round 31 item 2: a create under a secure parent would be BU-visible until provisioning moved it (round 28 E1 rejects such windows) |
| `SecureRootInheritance.PassUnshareOnAsync`, `ReconcileUnvisitedProvenanceAsync`, `EndProvenanceForUnsecureAsync` (+ private `GiveParentShareesAsync`, `EndInheritedSourceAsync`, `StillJustifiedByParentsAsync`; `InheritedUnsharePass`) | `PassSharesOnAsync` (the forward fan-out) | The reverse direction "in the same place 158 already calls PassSharesOnAsync" (round 30) and the job / unsecure hooks that keep the provenance true | A former sharee keeps filed records they should have lost (round 30's reason for (c)) |
| `SecureChildShareSynchronizer.IsolatedParentMirrorAsync` (+ `ParentMirrorAnswer`), `InheritedShareOutcome` / `InheritedShareAction`, `SecureChildShareSyncResult.Inherited/InheritedFrom`, `SyncInheritedRootAsync(declined)` | The synchronizer's private mirror read and `SyncInheritedRootAsync` | Same mirror computation exposed per parent; the outcomes are the provenance's input | Without per-principal outcomes the provenance would have to re-derive what the mirror wrote (racy) |
| Job: cursor, empty-flag scan, unvisited-provenance pass, ResultJson `emptyFlagParents` / `resumeAfter` / `unfiledProvenance`; `/unsecure-project` Step 3.6 (`inheritedSharesNotEnded`); `/share-user` / `/unshare-user` `filedRecordsNotUpdated` | the same job and endpoints | additive | the verifier's items 11-13 and round 30's "failures report through children_incomplete" |

The first r1 commit's `SecureChildShareSynchronizer.InheritedMirrorAsync` (`a58cbd8ee`) was REMOVED in r1c: replaced by
`StillJustifiedByParentsAsync` over `IsolatedParentMirrorAsync` (interpretation xv). `SecureFilingParent.Isolated` (and the
Secure-team read in `ReadParentAsync` that filled it) was REMOVED too: nothing ever read it (the verifier's A24 seed could
not bite it); every consumer of a parent's isolation asks the synchronizer when it acts.

## 8. Tests (KEEP paths, ADR-038)

- `tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceTests.cs` — the REAL routes (`/provision-project`,
  `/unsecure-project`), the REAL inheritance, job, reconciler and synchronizer over the provisioning fixture's Dataverse:
  transition (typed + pair in brace format; three decoys), cascade through a project, nesting bound + job completion,
  already-provisioned completion then 409, an unsecurable filed record → `children_incomplete` and left as it was,
  EMPTY flag unverifiable, secure-if-any with an unreadable sibling (shares held), intersection over two parents,
  add-only mirror + a later sharee, job one-run + second run no-op, scan failures (table fault, page ceilings) = failed
  run, an unreadable parent in the job, a flagged-not-isolated parent, the per-run provisioning bound, unsecure: stays
  secure + listed (type-exact; unconfirmed excluded), `alsoUnsecure` exactly-F3 / refused / not-related, still-secure
  parent 409 then allowed, unreadable parent 500.
- `tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs` — W1-W6 each through its own entry
  point with its own write seam applying the write to the fixture's Dataverse and the HOST's real gate: secure parent →
  secure (four facts read back), ordinary parent → unchanged, unreadable flag → refused and nothing written, unsecurable
  → reported (chat error / Office warning), an uninterpretable value refused, a host without the inheritance refuses a
  filing write (gate, node core, push) but not an ordinary write.
- `SecureChildOwnershipAiToolTests` — the chat create of a work assignment under a secure matter is now an ordinary app
  create of the caller's unit (was: refused), and an unreadable parent refuses. **r1:** superseded by round 31 item 2 —
  when the resolver's world says secure and the plan's says not, nothing is created.
- **r1 — `SecureRootInheritanceRound31Tests.cs`** (KEEP path, the REAL inheritance, job, provisioning route, share routes'
  handlers and synchronizer over the fixture's Dataverse and the 142 ledger double): the verifier's CRITICAL probe (an
  UNFLAGGED filed record whose creator is on its own list — nothing written, run reported) and its RESUME twin; the
  creator walled off the secure PARENT (job and transition); round 30 per path — share (provenance row: source, subject,
  mask), unshare-inherited (removed, Revoked), unshare-also-direct (covered / raised / independent Assigned-To row /
  another secure parent — kept), last reader (S5 — kept), raised share put back, operator-removed-then-parent-reshared
  (Declined, never re-added; ended with the parent's share; passed on again), out-of-band removal on the filed record
  (Declined), out-of-band unshare on the parent (job ends it; team sharee too), faults (provenance unreadable on share /
  unshare → children_incomplete; unwritable → failed run); r1c: unsecure ends the provenance so a re-secure passes the
  sharee on again (and stops when it cannot), a decline ends with the parent's share, an unreadable parent in the job's
  provenance step fails the run, an unsecured parent ends nothing, a re-filed-away record's inherited share ended by
  the job, the intersection on the way back (an untrusted second parent: removed when the read parent no longer carries
  it; held when every read parent does), an unreadable matter in the unshare fan-out; the job — cursor (records behind
  repeatedly refused ones are reached), deferral = not a success, EMPTY-flag parents scanned and reported, the pair
  found in N / P / X / padded / upper-case spellings, an unconfirmed pair candidate reported by job and transition (A2), a
  team-owned flagged record with no container completed (A6), an unreadable parent of a re-filed record fails the run.
- **r1 — `SecureRootCreateRouteTests.cs`** (KEEP path; AC 1's "a route driven end to end" for the CREATE paths): over the
  REAL host (TestServer: routing, auth, endpoint filters, handlers) with the real inheritance and provisioning —
  `POST /api/office/quickcreate/project` (201, created into isolation, secure; 403 without AppendTo) and
  `POST /api/ai/chat/sessions/{id}/gates/{gateId}/resolve` confirming `dataverse.create_record` (200 confirmed, secure;
  refused for a caller walled off the secure matter). Only module boundaries substituted (app-only Dataverse seams, the
  scripted OBO client, session and gate store, tool catalog).
- **r1 — `SecureRootInheritanceWriterTests.cs`** additions: chat / Office create INTO isolation (named team + flag in
  the create, no owner move), creator-share failure → row deleted and refused, container failure → secure for its
  creator and completed by the job, NEITHER shared NOR deletable → named (chat error / Office warning) and the job shares
  it; caller walled off the matter / an organization the record would reference / the list unreadable → refused; a
  secure+unreadable filing → refused; G5 (pair-named parent without AppendTo; Office without Create / AppendTo); a
  mapping rule targeting `sprk_issecure` skipped; resolver/plan disagreement → nothing created; re-file refused before the
  PATCH for a creator walled off the matter, off the record's own list, unnameable, or with a secure+unreadable filing.
- **r1 — `AssignedAccessReconciliationJobTests.ARootHoldingOnlyInheritedShareProvenance_IsNotACandidate`** (142's job).
- **r1 — harness fidelity:** `ProvisionProjectTestFixture` now registers the synchronizer SCOPED over the host's real
  `SecureShareNoAccessGuard` (as `ExternalAccessModule` does); it was a singleton over a guard that walls nobody, so the
  mirror's No Access guard was never exercised through this host (the verifier's A19 seed could not bite).

**r1 results (2026-10-04, `task/uac-r2-158-r1c`).** The 158 classes: **105/105** (`SecureRootInheritanceTests` 23,
`SecureRootInheritanceWriterTests` 37, `SecureRootInheritanceRound31Tests` 41 incl. a 5-case theory,
`SecureRootCreateRouteTests` 4). Affected (all of `tests/integration/data-mutation/ExternalAccess`, the chat tool
ownership tests, 142's job, share and marker classes, provisioning, unsecure): **674/674**. **Full BFF unit suite** (once,
at the end): **Passed 15324 / Failed 0 / Skipped 54** (Total 15378, 24 m 33 s). **NetArchTest:** **373/373**.
**Integration:** `Sprk.Bff.Api.IntegrationTests` **104/104**; `Spe.Integration.Tests` **403 passed / 25 skipped / 0
failed**. No contention failures this time.
- Construction sites of the four writers updated (`SecureRootFilingGateFixtures.NothingSecure()`); the provisioning
  fixture registers the REAL inheritance and mirrors each root's container; `SecureChildTransitionTests`' decoy child root
  is now filed under the root's EVENT (a work assignment filed directly under a secure project is task 158's to secure).

**Results (2026-10-04).** New: 40 test methods (41 cases) in the two KEEP-path classes, plus 2 in
`SecureChildOwnershipAiToolTests`. Affected suites (inheritance, writers, share endpoints, child transitions,
provisioning, unsecure, Office quick-create, chat tools, creator stamp, inbound association): 305/305, 182/182 (3 skipped,
pre-existing), 165/165. **Full BFF unit suite:** Passed 15007 / Failed 4 / Skipped 54 (Total 15065, 32 min, with other
worktrees running suites concurrently) — the 4 (`CitationVerificationServiceTests` cancellation,
`DocumentProfileContractTests` status 100000006, `ComposeMountPdfProjectionSeamTests` corrupt PDF,
`OfficeVersionSaveRevertTests` resend; each ~3 minutes under contention, none in code this task touches) **pass in
isolation: 9/9**. **NetArchTest:** first run 371/373 — two `RecordOwnerAssignmentCensusTests` findings, both fixed: the
unsecure owner write moved from `UnsecureProjectAsync` into `UnsecureRecordAsync` (census entry renamed), and the
156 × 146 merge's in-memory `RowAsWritten` in `IncomingAssociationResolver` read as a create (built with an explicit empty
id, the text `integ/uac-r2-batch4` already carries) — re-run **373/373**. **Integration:** `Sprk.Bff.Api.IntegrationTests`
104/104; `Spe.Integration.Tests` 403 passed / 25 skipped / 0 failed.

## 9. Seeds (each guard proven to bite, then restored byte-identical and touched)

**44 seeds, every one turned its named test(s) red**; each file was restored byte-identical from a backup copy of
`src/server/api/Sprk.Bff.Api` (verified file by file after every batch) and touched. Runtime-false conditions
(`x.Length < 0`, `recordId == Guid.Empty`) were used, never a constant `if (false)` (CS0162 is an error here).

| Seed | Guard removed | Tests that went red |
|---|---|---|
| S01 | `OwnedChildWrite`: a work assignment under a secure parent is refused again | chat create secure, chat create not-securable, AiTool ordinary-row test |
| S02 | the pre-check never refuses an unreadable parent | the 7 "flag cannot be read" tests (W1-W6 + AiTool) |
| S03 | an EMPTY parent flag read as not secure | the same 7 + `AParentWhoseFlagIsEmpty` |
| S04 | an unreadable sibling hides a readable secure parent | `ASecureParentBesideAnUnreadableOne` |
| S05 | sharees mirrored although a parent is unreadable | `ASecureParentBesideAnUnreadableOne` |
| S06 | the listing ignores the pair's TYPE | unsecure listing + transition |
| S07 | the unsecure list shows unconfirmed candidates | unsecure listing |
| S08 / S09 | unsecure under a STILL-secure / unreadable parent allowed | the 409 / 500 tests |
| S10 / S11 | `alsoUnsecure` without F3 / without the not-related check | `AlsoUnsecure_UnsecuresExactly…` |
| S12 | nesting bound raised | `AChainDeeperThanTheNestingBound` |
| S13a / S13b | inherited provisioning refuses an unflagged record / a CALLER may provision one | 16 inheritance tests / `ProvisionProject_WhenTheProjectIsNotSecure_IsRejectedAndWritesNothing` |
| S14 | the forward path skips the flag write | 15 tests (flag not set) |
| S15 / S16 / S17 | an incomplete filed pass reported as success / already-provisioned branch skips the pass / provisioning skips it | `AFiledRecordThatCannotBeSecured` + chain / `ProvisioningAnAlreadySecureMatterAgain` / transition + cascade |
| S18 / S19 / S20 | mirror replaces instead of adds / union instead of intersection / a flagged-not-isolated parent does not hold | add-only / two-parents / not-isolated tests |
| S21 / S22 / S23 | gate, node core, push let a filing write through without the inheritance | `WithoutTheInheritance_EveryWriterRefusesAFilingWrite` |
| S24 | an uninterpretable filing value not refused | `UpdateHandler_AFilingValueThatCannotBeInterpreted_IsRefused` |
| S25-S37 | each writer's pre-check and after-write call removed (chat create ×3 incl. "incomplete reported as success", chat update ×2, update handler ×2, Office ×2, push ×2, node core ×2) | that writer's refusal / securing tests |
| S38 / S39 | `/share-user` does not pass the sharee on / a project given sharees does not pass them on | `SharingAUserOnASecureMatter…` / cascade + `PassingShareesOn_IsBounded` |
| S40 / S41 | a sharee-only pass provisions / passing sharees on is unbounded | `SharingAUserOnASecureMatter…` / `PassingShareesOn_IsBounded` |
| S42 / S43 | a create's own columns dropped from the after-write call (chat / Office) | that writer's securing tests |

Two real defects were found while proving the guards: an uninterpretable filing value escaped as an exception instead
of a refusal (fixed: `CheckRefileAsync` catches it), and the secure-if-any rule was fail-OPEN when a second parent was
unreadable (fixed: interpretation iii). The full-suite run found a third: an unfiled project's create was READ back
after it was written (the Office contract tests warned and slowed) — a create now passes its own columns, so a row
filed under nothing costs no read (S42/S43 pin it).

### 9 r1c. Seeds of the fix round (the harness: `seeds158.py`, restored byte-identical from memory and touched)

Each seed removes ONE guard of rounds 30 / 31, the job's r1 changes or the verifier's batch (runtime-false
conditions, never a constant `if (false)`), builds (analyzers off for speed), runs the 158 classes + 142's job tests +
the share / mirror classes (330-336 tests), records what went red, and restores the file byte-identical from memory
(verified, then touched). **86 runs: 80 bit first time; the 6 that did not were each closed — a test added (R18,
R19, R37, J08), a harness gap fixed (J14: the provisioning fixture's synchronizer used a guard that walls nobody; it now
uses the host's real guard, as production registers it), or dead data removed (J10) — and re-seeded: all bit.** The
verifier's second batch: A3 = J12, A16 = J13, A18 = J07, A19 = J14 / J14b, A20 = R26, A21 = J08 / J08b, A22 = J09,
A23 = J11, A24 = J10 (removed). `git status -- src` clean after every batch.

| Seed | Guard removed | Result | Red (first; +n more) |
|---|---|---|---|
| R01 | provisioning asks the record's own list AS FLAGGED (the verifier's CRITICAL defect) | bit | `TheJob_ResumingARecordWhoseCreatorIsWalled_RefusesBeforeTheFlagOrTheShare` (+1) |
| R02 | provisioning skips the secure parents' No Access lists | bit | `ProvisioningAMatter_WhoseFiledRecordsCreatorIsWalledOffTheMatter_LeavesThatRecordAndReportsIt` (+1) |
| R03 | provisioning ignores a parent that cannot be read | bit | `ASecureParentBesideAnUnreadableOne_IsNotSecured_BecauseTheCreatorsWallsCannotBeChecked` |
| R04 | the RESUME branch shares a walled creator | bit | `TheJob_ResumingARecordWhoseCreatorIsWalled_RefusesBeforeTheFlagOrTheShare` |
| R05 | the FORWARD branch shares a walled creator | bit | `ProvisioningAMatter_WhoseFiledRecordsCreatorIsWalledOffTheMatter_LeavesThatRecordAndReportsIt` (+3) |
| R06 | CheckForSecuringAsync reads the flag (NotSecure on an unflagged record) | bit | `TheJob_ResumingARecordWhoseCreatorIsWalled_RefusesBeforeTheFlagOrTheShare` (+2) |
| R07 | the prospective create ignores the organizations its payload references | bit | `ChatCreate_WhenTheCallerIsWalledOffAnOrganizationTheRecordWouldReference_IsRefused_AndNothingIsCreated` |
| R08 | a re-file skips the recorded creator's walls | bit | `ChatUpdate_UnderASecureMatterWhileItsPairNamesAMatterWhoseFlagCannotBeRead_IsRefusedBeforeThePatch` (+3) |
| R09 | a re-file whose creator cannot be named proceeds | bit | `ChatUpdate_WhenTheRecordsCreatorCannotBeNamed_IsRefusedBeforeThePatch` |
| R10 | a re-file ignores an unreadable second parent | bit | `ChatUpdate_UnderASecureMatterWhileItsPairNamesAMatterWhoseFlagCannotBeRead_IsRefusedBeforeThePatch` |
| R11 | a re-file skips the secure parents' lists | bit | `ChatUpdate_WhenTheCreatorIsOnTheSecureMattersNoAccessList_IsRefusedBeforeThePatch` |
| R12 | a re-file skips the record's own list | bit | `ChatUpdate_WhenTheCreatorIsOnTheRecordsOwnNoAccessList_IsRefusedBeforeThePatch` |
| R13 | the plan never asks the writer's as-caller G5 | bit | `OfficeQuickCreate_WhenTheCallerCannotFileUnderTheSecureMatter_Is403_AndNothingIsCreated` (+4) |
| R14 | a create ignores an unreadable second parent | bit | `ChatCreate_UnderASecureMatterAndOneWhoseFlagCannotBeRead_IsRefused_AndNothingIsCreated` |
| R15 | a create skips the secure parents' lists | bit | `ChatCreate_ConfirmedThroughTheGate_ByACallerWalledOffTheSecureMatter_IsRefused_AndNothingIsCreated` (+2) |
| R16 | a create skips its own prospective list | bit | `ChatCreate_WhenTheCallerIsWalledOffAnOrganizationTheRecordWouldReference_IsRefused_AndNothingIsCreated` |
| R17 | a create proceeds when the list cannot be read | bit | `ChatCreate_WhenTheNoAccessListCannotBeRead_IsRefused_AndNothingIsCreated` |
| R18 | a create proceeds on an unresolved topology with a team id (weak) -- see note | not-bitten | NOT BITTEN (weak seed) -> test added, re-seeded as R18b |
| R19 | a create for nobody proceeds | not-bitten | NOT BITTEN -> test added (R19b) |
| R20 | an incomplete isolated create is always deleted (even when its creator can open it) | bit | `ChatCreate_WhenTheContainerCannotBeCreated_TheRecordStaysSecureForItsCreator_AndTheJobCompletesIt` |
| R21 | a row whose creator could not be shared is never deleted | bit | `ChatCreate_WhenTheCreatorCannotBeShared_TheIsolatedRowIsRemoved_AndNothingIsCreated` (+1) |
| R22 | a stranded row is not named | bit | `ChatCreate_WhenTheCreatorCannotBeSharedNorTheRowRemoved_SaysSo_AndTheJobSharesItToTheCreator` (+1) |
| R23 | chat: a removed row reported as created | bit | `ChatCreate_WhenTheCreatorCannotBeShared_TheIsolatedRowIsRemoved_AndNothingIsCreated` |
| R24 | chat: a stranded row reported as shared to you | bit | `ChatCreate_WhenTheCreatorCannotBeSharedNorTheRowRemoved_SaysSo_AndTheJobSharesItToTheCreator` |
| R25 | chat: a plan refusal loses its code | bit | `ChatCreate_ConfirmedThroughTheGate_ByACallerWalledOffTheSecureMatter_IsRefused_AndNothingIsCreated` (+7) |
| R26 | the isolated create does not name the Secure team | bit | `ChatCreate_ConfirmedThroughTheGate_OfAWorkAssignmentUnderASecureMatter_IsCreatedSecure_EndToEnd` (+2) |
| R27 | the isolated create is not flagged in the create | bit | `ChatCreate_AWorkAssignmentUnderASecureMatter_IsCreatedIntoIsolation_AndComesOutSecure` |
| R28 | resolver/plan disagreement creates a row | bit | `CreateRecord_AWorkAssignmentTheResolverPutsUnderTheSecureTeam_ButTheSecurePlanDoesNot_IsRefused_Task158r1` |
| R29 | the chat create never plans | bit | `ChatCreate_ConfirmedThroughTheGate_ByACallerWalledOffTheSecureMatter_IsRefused_AndNothingIsCreated` (+12) |
| R30 | G5 skips AppendTo on a pair-named secure parent | bit | `ChatCreate_UnderASecureMatterByThePair_WithoutAppendToOnTheMatter_IsDenied_AndNothingIsCreated` |
| R31 | Office: created into the caller's unit | bit | `OfficeQuickCreate_OfAProjectFromASecureMatter_IsCreatedSecure_EndToEnd` (+2) |
| R32 | Office: a removed project reported as created | bit | `OfficeCreate_WhenTheMakerCannotBeShared_TheProjectIsRemoved_AndTheCreateRefused` |
| R33 | Office: a stranded project warned as shared to you | bit | `OfficeCreate_WhenTheMakerCannotBeSharedNorTheProjectRemoved_WarnsSo` |
| R34 | Office: a mapping rule may write sprk_issecure | bit | `OfficeCreate_AMappingRuleTargetingTheSecureFlag_IsSkipped` |
| R35 | Office G5: no Create privilege check | bit | `OfficeCreate_WhenTheCallerCannotCreateProjects_IsRefused_AndNothingIsCreated` |
| R36 | Office G5: no AppendTo check | bit | `OfficeQuickCreate_WhenTheCallerCannotFileUnderTheSecureMatter_Is403_AndNothingIsCreated` (+1) |
| R37 | Office G5: no token → proceeds (would NRE/deny) | build-fail | BUILD-FAIL (nullable) -> re-seeded as R37b with a test |
| P01 | an unreadable provenance gives sharees anyway | bit | `SharingOnTheMatter_WhenTheProvenanceCannotBeRead_GivesNothing_AndReportsChildrenIncomplete` |
| P02 | a Declined row does not withhold the mirror | bit | `AnOperatorsRemovalOnTheFiledRecord_IsNeverUndoneWhileTheParentStillSharesIt` (+1) |
| P03 | an out-of-band removal on the filed record is not recorded Declined | bit | `TheJob_RecordsAnOutOfBandRemovalOnTheFiledRecordAsDeclined_AndNeverReAddsIt` (+1) |
| P04 | the job never ends a source the parent stopped sharing (out-of-band unshare) | bit | `ATeamSharee_IsPassedOn_RecordedWithItsTeam_AndEndedWhenTheMatterNoLongerSharesIt` (+2) |
| P05 | an unreadable parent in the job's provenance step is silently skipped | bit | `TheJob_WhenAParentThatPassedAShareOnCannotBeRead_FailsTheRun_NamingTheRecord` |
| P06 | provenance not recorded | bit | `ATeamSharee_IsPassedOn_RecordedWithItsTeam_AndEndedWhenTheMatterNoLongerSharesIt` (+22) |
| P07 | a provenance write failure reported as complete | bit | `TheJob_WhenTheProvenanceCannotBeWritten_FailsTheRun` |
| P08 | an undecided intersection (untrusted parent) treated as justified | bit | `UnsharingFromTheMatter_HoldsTheShare_WhenEveryReadParentCarriesItButAnotherCannotBeTrusted` |
| P09 | a read parent that no longer carries the person does not end it | bit | `ATeamSharee_IsPassedOn_RecordedWithItsTeam_AndEndedWhenTheMatterNoLongerSharesIt` (+7) |
| P10 | an untrusted parent does not make the intersection undecided | bit | `UnsharingFromTheMatter_HoldsTheShare_WhenEveryReadParentCarriesItButAnotherCannotBeTrusted` |
| P11 | an independent Assigned-To row does not keep the share | bit | `UnsharingFromTheMatter_KeepsAShareAnIndependentLedgerRowAlsoJustifies` |
| P12 | a modified inherited share is removed | bit | `UnsharingFromTheMatter_KeepsAnInheritedShareThatWasRaisedSince` |
| P13 | S5: the last reader is removed | bit | `UnsharingFromTheMatter_NeverRemovesTheFiledRecordsLastReader` |
| P14 | a raised share is revoked instead of put back | bit | `UnsharingFromTheMatter_PutsARaisedShareBackToWhatItWasBefore` |
| P15 | a direct (covered) share is not recorded kept-direct | bit | `UnsharingFromTheMatter_KeepsADirectShareThatAlreadyCarriedTheMirror` |
| P16 | an unsecure leaves the inherited rows live | bit | `UnsecuringAFiledRecord_EndsWhatItsParentPassedOn_SoSecuringItAgainPassesTheShareeOnAgain` (+1) |
| P17 | the unsecure never ends the provenance | bit | `UnsecuringAFiledRecord_EndsWhatItsParentPassedOn_SoSecuringItAgainPassesTheShareeOnAgain` (+1) |
| P18 | /unshare-user on a parent that is no longer secure ends what it passed on | bit | `UnsharingFromAMatterThatIsNoLongerSecure_EndsNothingItPassedOn` (+1) |
| P19 | /unshare-user fan-out on an unreadable parent reported as done | bit | `UnsharingFromTheMatter_WhenTheMatterCannotBeReadForTheFanOut_ReportsTheFiledRecordAsNotUpdated` |
| P20 | /unshare-user does not fan out | bit | `AnOperatorsRemovalOnTheFiledRecord_IsNeverUndoneWhileTheParentStillSharesIt` (+11) |
| P21 | /unshare-user fan-out failures not reported | bit | `UnsharingFromTheMatter_HoldsTheShare_WhenEveryReadParentCarriesItButAnotherCannotBeTrusted` (+1) |
| P22 | /share-user fan-out failures not reported | bit | `SharingOnTheMatter_WhenTheProvenanceCannotBeRead_GivesNothing_AndReportsChildrenIncomplete` |
| P23 | the job does not reconcile records re-filed away | bit | `TheJob_EndsAnInheritedShareOnARecordReFiledAwayFromTheParent_WhenTheParentUnsharesOutsideTheBff` (+1) |
| P24 | the reconcile looks at the visited records instead | bit | `TheJob_EndsAnInheritedShareOnARecordReFiledAwayFromTheParent_WhenTheParentUnsharesOutsideTheBff` (+1) |
| P25 | the reconcile silently skips an unreadable parent | bit | `TheJob_WhenAParentOfAReFiledRecordCannotBeRead_FailsTheRun_AndEndsNothing` |
| P26 | the job reports success with the reconcile incomplete | bit | `TheJob_WhenAParentOfAReFiledRecordCannotBeRead_FailsTheRun_AndEndsNothing` |
| P27 | 142's scan counts inherited rows | bit | `ARootHoldingOnlyInheritedShareProvenance_IsNotACandidate` |
| P28 | the synchronizer re-adds a declined principal | bit | `AnOperatorsRemovalOnTheFiledRecord_IsNeverUndoneWhileTheParentStillSharesIt` (+2) |
| P29 | a parent that is no longer isolated is treated as isolated | bit | `UnsharingFromAMatterThatIsNoLongerSecure_EndsNothingItPassedOn` (+1) |
| J01 | A5 / item 11: deferral reported as success | bit | `TheJob_ProvisionsAtMostItsBoundPerRun_AndTheNextRunSecuresTheRest` |
| J02 | the cursor is ignored | bit | `TheJob_ContinuesFromItsCursor_SoRecordsBehindOnesThatKeepFailingAreReached` |
| J03 | item 12: empty-flag parents not scanned | bit | `TheJob_ReportsARecordFiledUnderAParentWhoseFlagIsEmpty` |
| J04 | item 13: only the D spelling is listed | bit | `TheJob_FindsAPairInEverySpellingTheDecisionAccepts` (+1) |
| J05 | A2: unconfirmed pair candidates dropped | bit | `AFiledRecordWhosePairTypeCannotBeRead_IsReportedByTheJobAndTheTransition_AndNothingIsWritten` |
| J06 | A6: isolation ignores the container | bit | `TheJob_CompletesAFiledRecordThatIsTeamOwnedAndFlaggedButHasNoContainer` (+2) |
| J07 | A18: pair type ignored | bit | `ProvisioningAMatter_SecuresTheWorkAssignmentsAndProjectsFiledUnderIt_AndGivesThemItsSharee` (+1) |
| J08 | A21: parent-flag check in the pass removed | not-bitten | NOT BITTEN: equal behaviour except reads / an EMPTY flag -> J08b, J08c with tests; an EMPTY flag no longer short-circuits |
| J09 | A22: an EMPTY parent flag read as not secure | bit | `TheJob_ReportsARecordFiledUnderAParentWhoseFlagIsEmpty` (+10) |
| J10 | A24: parent isolation ignores the owner | not-bitten | NOT BITTEN: SecureFilingParent.Isolated was never read by anything -> REMOVED (dead data), no guard left |
| J11 | A23: after-write skips every write that names its columns | bit | `ActionCore_RefilingUnderASecureMatter_SecuresIt` (+3) |
| J12 | A3: /share-user does not pass the sharee on | bit | `AnOperatorsRemovalOnTheFiledRecord_IsNeverUndoneWhileTheParentStillSharesIt` (+14) |
| J13 | A16: provisioning Step 8 skipped | bit | `AFiledRecordWhosePairTypeCannotBeRead_IsReportedByTheJobAndTheTransition_AndNothingIsWritten` (+5) |
| J14 | A19: the root mirror skips the walls | not-bitten | NOT BITTEN: the fixture synchronizer walled nobody -> host guard wired, test added, J14b |
| R18b | a create proceeds into isolation with no resolved Secure team | bit | `ChatCreate_WhenTheSecureOwnerTeamCannotBeResolved_IsRefused_AndNothingIsCreated` |
| R19b | a create for nobody proceeds (re-run) | bit | `ThePlan_ForACreateUnderASecureMatterForNobody_Refuses` |
| R37b | Office G5: no bearer token → asked anyway | bit | `OfficeCreate_WhenTheCallersRightsCannotBeChecked_IsRefused_AndNothingIsCreated` |
| J08b | A21: a pass over an ORDINARY parent reads what is filed under it | bit | `SharingOnAnOrdinaryMatter_ReadsNothingFiledUnderIt` |
| J08c | round 17: an EMPTY parent flag read as 'nothing to pass on' on the share route | bit | `SharingOnAMatterWhoseFlagIsEmpty_ReportsTheFiledRecordsAsNotUpdated` |
| J14b | A19: the root mirror skips the walls (re-run with the host's real guard) | bit | `TheJob_NeverPassesOnASharee_WhoIsOnTheFiledRecordsNoAccessList` |


## 10. Manual live gates (main session; round 11 approved them at integration) — exact commands

Writes, so pending.

0. **Gate 158-0 (r1, BEFORE the BFF deploy — schema).** The ledger's `sprk_subjectteam` lookup (round 30). Without it
   every inherited-share read fails closed: `/share-user` / `/unshare-user` on a secure matter or project answer
   `children_incomplete` and pass nothing on, the job's runs fail, an unsecure of a work assignment or project stops
   before revoking anything. Task 142's own gate creates the table; this adds one lookup (idempotent, re-runnable):
   ```powershell
   pwsh -File scripts/Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com           # dry run: expect WOULD create sprk_subjectteam
   pwsh -File scripts/Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
   pwsh -File scripts/Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify   # exit 0; (g) the select incl. _sprk_subjectteam_value answers
   ```
   Then deploy the BFF. The remaining gates run after the deploy.

1. **Gate 158-a (the POML's live gate).** Create a work assignment filed under the secure test project, through a BFF
   path (chat in the Console: *"create a work assignment 'UAC 158 live gate' regarding project Test New Matter via
   Workspace"* → `dataverse.create_record`) **or** out of band and wait one job cycle:
   ```powershell
   $org = "https://spaarkedev1.crm.dynamics.com"; $t = az account get-access-token --resource $org --query accessToken -o tsv
   $nav = (Invoke-RestMethod -Headers @{Authorization="Bearer $t"} "$org/api/data/v9.2/EntityDefinitions(LogicalName='sprk_workassignment')/ManyToOneRelationships?`$filter=ReferencingAttribute eq 'sprk_regardingproject'&`$select=ReferencingEntityNavigationPropertyName").value[0].ReferencingEntityNavigationPropertyName
   Invoke-RestMethod -Method Post -Headers @{Authorization="Bearer $t"; "Content-Type"="application/json"} "$org/api/data/v9.2/sprk_workassignments" -Body (@{ sprk_name = "UAC 158 live gate"; "$nav@odata.bind" = "/sprk_projects(65a3fab2-77a5-f111-aaad-70a8a590c51c)" } | ConvertTo-Json)
   ```
   Then (read-only, after ≤ 5 minutes — check the `[SECURE-INHERIT] heartbeat` log line):
   ```powershell
   Invoke-RestMethod -Headers @{Authorization="Bearer $t"} "$org/api/data/v9.2/sprk_workassignments?`$filter=sprk_name eq 'UAC 158 live gate'&`$select=sprk_workassignmentid,sprk_issecure,_owningteam_value,sprk_containerid"
   # expect sprk_issecure = true, _owningteam_value = the named "Secure Record Owners" team, a container of its own
   # (not b!MVasATu…), and the creator shared:
   $wa = "<sprk_workassignmentid from above>"
   Invoke-RestMethod -Headers @{Authorization="Bearer $t"} "$org/api/data/v9.2/RetrievePrincipalAccess(Target=@tid,Principal=@pid)?@tid={'@odata.id':'sprk_workassignments($wa)'}&@pid={'@odata.id':'systemusers(d6f8f439-40bf-f111-a05b-3833c5e9614d)'}"
   # expect AccessRights WITHOUT ReadAccess for uac.child.user (non-admin, child BU, no share): DENIED
   ```
   r1: through the chat path the row is created INTO isolation — expect, in addition, `_createdby_value` = the BFF
   application user, `_sprk_createdbyperson_value` = the person who asked, and NO owner change in the record's audit
   history after the create (there was never a business-unit-visible window). Through the out-of-band path the job
   secures it as before. Also confirm the provenance (read-only): the secure test project's sharees appear on the work
   assignment, each with a ledger row:
   ```powershell
   Invoke-RestMethod -Headers @{Authorization="Bearer $t"} "$org/api/data/v9.2/sprk_assignedaccesses?`$filter=_sprk_workassignment_value eq $wa and startswith(sprk_sourcefield,'inherited:')&`$select=sprk_sourcefield,sprk_state,sprk_grantedlevel,_sprk_subjectsystemuser_value,_sprk_subjectteam_value"
   # expect one row per sharee of 65a3fab2: sourcefield inherited:sprk_project:65a3fab2-77a5-f111-aaad-70a8a590c51c,
   # sprk_state 100000001 (Shared) or 100000002 (CoveredByExisting — already held it), sprk_grantedlevel the mask written
   ```
2. **Gate 158-b (unsecure listing).** After 158-a, `POST /api/v1/external-access/unsecure-project` is NOT run on the
   shared test project (it would unsecure it); verify the listing on a throwaway secure matter instead (create, provision,
   file a work assignment, unsecure the matter with no `alsoUnsecure`, confirm the response lists the work assignment and
   it stays secure; then unsecure it alone).
3. **No first-run effect:** §2b — 0 candidates in dev today; re-run the §2b counts immediately before deploy.

## 11. Questions for the owner / main session — ALL ANSWERED (r1)

- **Q1 → round 30** (option (c), provenance on task 142's ledger): implemented in full (§4, §14).
- **Q2 → owner round 32** (§6.5 path B, secure inline — ACCEPTED): the handler remarks say ACCEPTED; Amendment A-UAC158
  is recorded beside A-UAC156 (round 8 item 1) / round 13 item 7 in spaarke-ai-architecture-redesign-r1's spec (a
  ⚠️ bullet in its MUST rules and the full text under ADR Tensions); the PR's §6.5 block cites owner round 32.
- **The first verification's question** ("should the creator share also honour the secure PARENT's No Access list?")
  **→ round 31 item 1: yes**, before any write, never flag-dependent (§3, §14).
- No question is open. The original text of Q1 / Q2 follows for the record.

- **Q1 (interpretation iv).** When a sharee is removed from a secure parent, should they also lose the filed records the
  parent's sharing gave them? Today: **no** (add-only; they keep it until someone removes it on the record). Options:
  (a) keep add-only (recommended for now: never removes an intentional grant, never strands a record — S5);
  (b) revoke the same principal from every filed record on a parent unshare (fail closed; may remove an intentional
  direct grant; never the record's last person); (c) record provenance for mirrored shares (a schema change).
- **Q2 (decision viii).** Confirm the §6.5 path-B extension: the user-OBO chat tools (`dataverse.update_record`'s re-file;
  `dataverse.create_record` is already app-only under round 7 item 3) run provisioning's app-only steps inline after the
  caller's own write. Record beside Amendment A-UAC146 / round 8 item 1 in spaarke-ai-architecture-redesign-r1's spec.

  **ADR Conflict — Resolution Required** (CLAUDE.md §6.5)
  - *Rule in question:* spaarke-ai-architecture-redesign-r1 spec, the user-OBO rule for the chat Dataverse tools ("the
    update tool MUST NOT reach an app-only client"), as amended by owner round 7 item 3 (creates, A-UAC146), round 8
    item 1 (the 156 re-stamp) and round 13 item 7 (the 146 re-file owner step).
  - *Conflict:* round 6 makes a work assignment re-filed under a secure matter secure; doing it in the same operation
    needs provisioning's app-only writes (flag, creator share, owner move, container) after the caller's own PATCH.
  - *Proposed path:* **B (amendment)** — the same class of follow-on as round 8 item 1 and round 13 item 7: the caller's
    own write stays user-OBO and runs first; only provisioning's own steps (one narrow seam, `SecureRootFilingGate`) run
    app-only, for the record the caller just wrote, for its recorded creator.
  - *Impact:* `DataverseUpdateRecordHandler` (and the create tool, already app-only) reach provisioning through the gate;
    nothing else.
  - *Alternatives considered:* (C) comply — leave chat re-files to the job: the record would be BU-visible for up to 5
    minutes after a user explicitly filed it under a secure matter, and the tool would report success for a record that
    is not secure (rejected: ADR-003 and R3/R4); (A) project exception — the amendment record already exists and this is
    the same reasoning, so B keeps one record.
  - *Implemented as proposed* — and **ACCEPTED by owner round 32** (2026-10-04); the class remarks of
    `DataverseUpdateRecordHandler` / `DataverseCreateRecordHandler` now say ACCEPTED.

## 12. Handovers

- **Task 150 (ribbon / Access group, round 26):** render `relatedSecureRecords` with a checkbox each; post the ticked
  ones in `alsoUnsecure`; show `relatedRecordsUnsecured` outcomes; map `sdap.unsecure.parent_still_secure` (409, names the
  parent) and `sdap.unsecure.parent_unverifiable` (500) to copy. The provisioning wizard already handles
  `children_incomplete`; `filedRecords` is additive.
  r1: the unsecure's new Step 3.6 stop answers the EXISTING `sdap.unsecure.children_incomplete` (500, retryable — its
  client copy already applies; the extension `inheritedSharesNotEnded: true` is additive), and the share/unshare
  `children_incomplete` bodies gain `filedRecordsNotUpdated` (additive). A create refusal through the Office route uses
  the existing `sdap.provision.creator_no_access*` codes (round 29 copy) plus `caller_cannot_create` /
  `caller_cannot_file_under_parent` (403) and `caller_rights_unverifiable` (500), whose `detail` is user-readable.
  r1c-v2: the unsecure of a matter / project can now ALSO answer the existing 500 `sdap.unsecure.children_incomplete`
  from Step 4.5 (flag kept — "calling again completes it") or Step 5.5 (the record unsecured — "given back automatically
  within a few minutes"), each with the additive extension `filedRecordsNotUpdated`; its client copy already applies (the
  detail is user-readable). `/share-user` and the provisioning colleague warning name the secure matter's / project's list
  when it is a parent's list that refuses (the same codes — round 39 item 2).
- **Task 166:** `POST /api/v1/work-assignments` deletion (and its census waiver) as planned; nothing here depends on it.
- **Main session:** §10 gates; §11 decisions; §13 merge points.

## 13. Integration notes (hand-merge points)

- **Task 150** (`EnsureSecureFlagAsync`, Step 4.1, the unsecure F3 gate): this branch carries 150's
  `EnsureSecureFlagAsync` text verbatim and calls it in the forward path (Step 4.1) and the resume path; 150 makes Step 1
  refuse an unflagged record for a CALLER — this branch keeps that and admits an unflagged record only for
  `ProvisioningCreator.RecordedCreator` (inherited provisioning). Keep ONE copy of the helper and ONE F3 check on
  unsecure (150's gate uses the same `SecureDesignationRemoval`).
- **Task 146-c1** (`SecureDesignationRemoval.cs`, `IDataverseRecordShareService.GetPrincipalRightsAsync`): identical
  content — a clean add/add.
- **156 × 146 merge** (done here, commit `ab4895ea3`): `DataverseUpdateHandler` / `UpdateRecordActionCore` /
  `DataverseUpdateRecordHandler` / `EventEndpoints` / document + record-match routes carry BOTH the 146 ownership resolver
  and the 156 restamper (restamp after the write, after a re-file stands); `IncomingAssociationResolver` takes 156's
  `ApplyCoreAncestorStampsAsync`; the I-1 / I-2 registry rows keep both texts.
- **Task 142** (`InternalShareEndpoints`): `ShareAsync` gains `SecureRootInheritance relatedRoots` (after
  `noAccessGuard`) and `ChildrenIncompleteAfterShareAsync` calls `relatedRoots.PassSharesOnAsync(...)` right after the
  child `SyncRootAsync`, before its completeness check. Keep both 142's changes and this call.
- **Census** (`RecordOwnerAssignmentCensusTests`): the unsecure owner-write entry is now member `UnsecureRecordAsync`;
  `IncomingAssociationResolver.RowAsWritten` builds its in-memory row with an explicit empty id — the same text as
  `integ/uac-r2-batch4` (clean merge).
- **r1 — task 142** (`AssignedAccessStore`, `Set-AssignedAccessLedgerSchema.ps1`, `AssignedAccessTestDoubles`): this
  branch merged `task/uac-r2-142-r6` (`5d041a627`) and extends the ledger (inherited-row members, `SubjectTeamId`, four
  reason codes + `record-unsecured`, `sprk_subjectteam` in the schema script with the batch-4 solution-membership helper
  `scripts/common/DataverseSolutionMembership.ps1` byte-identical) and changes ONE 142 behaviour on purpose:
  `ScanLedgerRootsAsync` leaves inherited rows out (`IsAssignedToScanRow`; pinned by
  `AssignedAccessReconciliationJobTests.ARootHoldingOnlyInheritedShareProvenance_IsNotACandidate`). If 142 lands a later
  round first, re-apply these on top. `InternalShareEndpoints.UnshareAsync` gains `SecureRootInheritance relatedRoots`
  (after `secureChildShares`) and calls `PassUnshareOnAsync` after the child pass.
- **r1 — task 150** (`UnsecureProjectEndpoint`): Step 3.6 (end the inherited provenance) sits between 148's child pass
  (3.5) and the share sweep (4); 150's unsecure F3 gate runs before Step 1 — no overlap. `ProvisionProjectEndpoint`'s
  creator check before Step 4.1 is now `CheckCreatorWallsAsync` on BOTH the forward and the resume path: keep it BEFORE
  150's `EnsureSecureFlagAsync` (the flag write) when merging 150's text.
- **r1c-v1 — task 142** (`AssignedAccessStore`, `AssignedAccessTestDoubles`): `CreateInheritedLedgerAsync` now answers
  `Guid?` (`null` = lost the alternate-key race, nothing written); `ScanLedgerRootsAsync` lost its in-memory
  `.Where(IsAssignedToScanRow)` and `IsAssignedToScanRow` is gone (the fake keeps the predicate inline); two reason
  constants (`SharePending`, `KeptLastReader`'s doc). The fake gains `BeforeInheritedCreate`, `InheritedCreateConflicts`,
  `SeedInheritedRow`, `FailLedgerUpdates`; `GrantPolicyTestDoubles.SeamNoAccessListReader.Lift`. If 142 lands a later
  round first, re-apply on top. `InternalShareEndpoints.ChildrenIncompleteAfterUnshareAsync` gains the give-back sentence
  and counts `filed.NotRegiven` into `filedRecordsNotUpdated` (142's / 149's code around it unchanged).
- **r1c-v1 — task 149** (`SecureChildShareSynchronizer.SyncInheritedRootAsync`): one optional parameter (`recordIntent`)
  and the write-ahead call before each grant/raise; nothing else in 149's passes changes.
- **r1c-v1 — task 140 (round 42)**: E-158-v1-2's complete fix uses the `If-Match` support round 42 adds to the grant
  writes; when 140 merges, the ledger updates can adopt it (escalated, §15.1).
- **r1c-v2 — task 142** (`AssignedAccessMaterializer`, `AssignedAccessStore`, `AssignedAccessTestDoubles`): the materializer
  gains an optional last constructor parameter `IServiceScopeFactory? scopes` (round 47 item 1 (3): after an assignment ended
  on a work assignment / project it runs `SecureRootInheritance.PassShareesToFiledRecordAsync` in a scope — the inheritance
  depends on the materializer, so not a constructor dependency back), FreshShareAsync's wall check is the guard's
  `CheckRecordAndSecureParentsAsync` (round 39 item 2), and a new ledger reason `AssignedAccessReason.CoveringShareEnded`
  (round 47 item 1 (2); `sprk_reason` is free text — no schema change). The harness gains `Entities`, `GuardOverride`,
  `SharesOverride`, `Scopes` and `NoFilingRows()`; the fake store `BeforeInheritedRead` and `InheritedByParentTruncated`.
  `InternalShareEndpoints.UnshareAsync`: `MarkShareRemovedAsync` moved BEFORE the revoke (round 47 item 2). If 142 lands a
  later round first, re-apply these on top.
- **r1c-v2 — task 143** (`SecureShareNoAccessGuard`): one constructor parameter added (`IGenericEntityService`, the filing
  walk) and the new entry point `CheckRecordAndSecureParentsAsync` (two overloads) + `SecureWallRecordScope` +
  `SecureShareWallDecision.ParentTable/ParentId` (init). Every manual construction in tests passes
  `AssignedAccessTestDoubles.NoFilingRows()` (or a world). `ExternalAccessContractTests`' host registers the guard over a
  no-rows filing world (its `IGenericEntityService` is the production one, which has no Dataverse there).
- **r1c-v2 — task 149** (`SecureChildShareSynchronizer`): `ParentMirrorAnswer.PassesNothingOn` / `NotSecure`, and
  `RootFacts.Flag` (the raw flag); `IsolatedParentMirrorAsync` answers NotSecure for a parent that is gone or reads false,
  CouldNotRead for an EMPTY flag. Nothing in 149's own passes changes.
- **r1c-v2 — task 150** (`UnsecureProjectEndpoint`): Steps 4.5 and 5.5 between 148's Step 4 and the flag, and the
  already-ordinary path's leftover pass — keep them when merging 150's unsecure F3 gate (which runs before Step 1).
- **r1c-v2 — integration checklist, after task 140 merges (round 47 item 2, BINDING; not this lane's to do before then):**
  every ledger UPDATE a pass makes (`RecordIntentAsync`, `RecordProvenanceAsync`, `ConfirmAsync`, `EndRowAsync`, the (a)
  markers, `ReopenAsync`, the covering-share-ended marker) sends `If-Match` with the ETag of the row read it decided on —
  through task 140's If-Match support on `DataverseWebApiClient` (round 42), never a second mechanism. `AssignedAccessLedgerRow`
  gains the `@odata.etag`; `AssignedAccessStore.UpdateLedgerAsync` takes it; a 412 re-reads the row and re-decides (the pass
  returns "decided on the next pass" for that principal, never a blind write). Tests + seeds: an operator's Declined marker
  landing between a pass's read and its write-ahead update makes that update fail 412 and NO share is written; the same for
  the confirmation and the reverse rule's end. This closes the rest of E-158-v1-2 (a pass that read the row before the
  marker); the marker-before-revoke half is done here.
- `.claude/` edits needed: **none**.

## 14. Fix round r1 (rounds 30, 31, owner round 32 and the first verification) — item by item

Branch `task/uac-r2-158-r1c` from `wip/uac-r2-158-r1-restart` (`c5decdbeb`). The first r1 agent's commit `a58cbd8ee`
(rounds 30 + 31, the job and the pair listing) and its WIP `c5decdbeb` (saved before a machine restart, unverified) are
the base; r1c verified both, kept them, and finished the round.

| # | Item | Outcome |
|---|---|---|
| 1 | The WIP: decide what is sound, keep it, finish | Kept, all of it: G5 asked by the plan BEFORE any No Access answer is given (a caller who may not file under a secure record learns nothing more about it); the two CREATE routes driven end to end (`SecureRootCreateRouteTests`); the Office guard that a mapping rule may not write `sprk_issecure`; two more round-30 tests; the I-11 row and guide row 15. It built and its 119 tests passed as found. Finished: the defects below, the tests and seeds, this note, the POML, the spec bullet |
| 2 | Round 30 in full | Provenance on task 142's ledger (no new table): every share `PassSharesOnAsync` / provisioning / the job passes on is a row on the filed root (`inherited:{parentTable}:{parentId}`, user or team subject, the mask written); `sprk_subjectteam` added to `Set-AssignedAccessLedgerSchema.ps1` (dry run / `-Apply` / `-Verify`, solution-membership helper; gate 158-0) and to the store. A4's rules: never lower (a raise is put back to its prior level); only the UNMODIFIED inherited share is removed; a share that is also direct (covered, raised, an independent Assigned-To row, another parent's intersection) is kept; an operator's removal is `Declined`, never re-added while the parent's share persists, and ends with it; failures → `children_incomplete`; the job reconciles (out-of-band removals on the record, out-of-band unshares on the parent, records re-filed away). **r1c found and fixed four gaps in the first r1 commit:** (a) an unsecure of a filed record left its rows `Shared`, so a re-secure read every revoked share as Declined and withheld the parents' sharees for good — Step 3.6; (b) a `Declined` row never ended on an out-of-band parent unshare, so a later re-share was never passed on; (c) a parent whose state could not be read was read as "not isolated, ends nothing" — now reported; (d) any untrusted second parent made the reverse rule keep the share — now decided on the intersection with task 149's rule (interpretation xv). Also: a record re-filed away is reconciled; `/unshare-user` on a parent that is no longer secure ends nothing (xiii, xiv); 142's job scan no longer counts inherited rows (xvii). Tests + seeds per path: share, unshare-inherited, unshare-also-direct (4 kinds), operator-removed-then-parent-reshared, fault (5) |
| 3 | Round 31 in full | Item 1: `CheckCreatorWallsAsync` (record + every secure parent, `CheckForSecuringAsync` — never flag-dependent) on the forward and resume paths before the first write; the re-file pre-check refuses a walled / unverifiable / unnameable recorded creator before the PATCH; a create refuses a caller walled off a secure parent or its own prospective list (`CheckProspectiveAsync`, the payload's organizations) or not checkable. Item 2: created INTO isolation by the app, owned by the named team, flag and `sprk_createdbyperson` in the create, after G5 (incl. AppendTo on a pair-named parent; Office: OBO Create + AppendTo); creator share read back by provisioning's re-entry branch; a creator share not in place → the row deleted (read back) and the create refused; a container / sharee step not done → provisioned-but-incomplete, completed by the job. r1c: the creator is carried from the plan (no second WhoAmI, whose failure would have named nobody and deleted a row its creator can open); a row that can be neither shared nor deleted is named, never "shared to you" (xviii) |
| 4 | Owner round 32 | ACCEPTED in the handler remarks (`DataverseUpdateRecordHandler` (3), `DataverseCreateRecordHandler`), the POML and §6 (viii) / §11; Amendment A-UAC158 recorded in spaarke-ai-architecture-redesign-r1's spec — a ⚠️ bullet beside A-UAC156 (round 8 item 1) in the MUST rules and the full text beside A-UAC146 under ADR Tensions, citing owner round 32 and round 13 item 7 |
| 5 | CRITICAL: no N6 check on an unflagged inherited record | Fixed (`CheckForSecuringAsync`); the verifier's probe is `TheJob_WhenARecordsCreatorIsOnItsOwnNoAccessList_NeitherSecuresNorSharesIt_ThoughItReadsUnflagged` (mask 0, nothing written, run reported) and its resume twin; seeds R01, R04, R05, R06 |
| 6 | MEDIUM: the creator ignored the secure PARENT's list | Fixed (round 31 item 1) on every path; seeds R02, R11, R15 |
| 7 | MEDIUM: A2 unpinned | Pinned: `AFiledRecordWhosePairTypeCannotBeRead_IsReportedByTheJobAndTheTransition_AndNothingIsWritten`; seed J05 |
| 8 | MEDIUM: A6 unpinned | Pinned: `TheJob_CompletesAFiledRecordThatIsTeamOwnedAndFlaggedButHasNoContainer`; seed J06 |
| 9 | The verifier's second seed batch (A3, A16, A18-A24) | Re-run here: J12 (A3), J13 (A16), J07 (A18), J14 (A19), R26 (A20), J08 (A21), J09 (A22), J11 (A23), J10 (A24) — results in §9 |
| 10 | LOW: no CREATE route driven end to end | `SecureRootCreateRouteTests`: the Office quick-create route and the chat confirmation-gate route, over the real host |
| 11 | LOW: deferral reported as success | Any deferral → `Success = false`; a cursor so the next run continues after the last record reached (records behind repeatedly failing ones are not starved); seeds J01, J02 |
| 12 | LOW: NULL-flag parents never scanned | Scanned (`ConditionOperator.Null`) and their filed records reported unverifiable (`emptyFlagParents`); seed J03 |
| 13 | LOW: pair listing only D / B spellings | One `LIKE` on the first 8 hex digits (present verbatim in every spelling `Guid.TryParse` accepts), then the decision side's parse; tested N, P, X, padded, upper; seed J04 |
| 14 | LOW: misplaced XML doc comment | Fixed: `ProvisioningCreator` has one summary, `OwnerMoveOutcome` its own |
| 15 | LOW: the BU-visible window on create | Gone: round 31 item 2 (interpretation i superseded) |
| 16 | Verified OK | Re-checked: `SecureDesignationRemoval.cs` blob `13ba8f80b` unchanged |
| 17 | The verifier's suites | Re-run here in full (§8, r1 results) |
| 18 | `C:\wvd158` | Removed (`git worktree remove --force C:\wvd158`). It still held the verifier's probe file and an UNRESTORED seed in `InternalShareEndpoints.cs` (the restart killed its harness); nothing of it reached a branch. The probe's four cases are covered by committed tests |
| 19 | AC1 not met | Met: item 10 |
| 20 | AC4 not met | Met: items 11, 12 (a run with more than 25 candidates is not a success; the next run continues) |
| 21 | AC5 / N6 not met | Met: item 5 |
| 22 | AC8 not met | Met: every new branch seeded (§9, r1c table); the full BFF unit suite and NetArchTest re-run (§8) |
| 23 | AC9 manual live gate | Pending for the main session, as expected (§10: 158-0 schema BEFORE the deploy; 158-a, 158-b after) |
| 24 | The verifier's question | Answered by round 31 item 1 (yes) and implemented (item 6) |

## 15. Fix round r1c-v1 (the verification of `task/uac-r2-158-r1c`) — item by item

Branch `task/uac-r2-158-r1c-v1` from `task/uac-r2-158-r1c` (`25b7ac3dd`). Binding: rounds 30, 31, owner round 32, round 39
(the second verification), round 3 S5 / A4; the standing directive (round 15). No owner question was needed; the
interpretations this round adds are §6 xx–xxv (owner-reversible). **Correction of r1c's own claim:** §9 r1c said "every
guard bites". It did not: the verifier's V18 (the delete read-back), W13 (the reverse rule's mask comparison), W03 (the
decline-set removal) and W28 (the scan's in-memory filter) bit nothing. Each is closed below.

| # | Verifier item | Outcome |
|---|---|---|
| 1 | MEDIUM, CONFIRMED: after a provenance-write fault (or a share/job race) the retry recorded the inherited share as DIRECT (`CoveredByExisting`) — the run said success and the parent's unshare never removed it | **Fixed — write-ahead provenance (§4, §6 xx–xxii).** `SyncInheritedRootAsync` gains a `recordIntent` hook called right BEFORE each grant/raise; `SecureRootInheritance.RecordIntentAsync` writes one `Shared` row per isolated parent with reason `share-pending` (+ `;raised-from-mask:N`) and the mask about to be written, and answers `false` — the share is then NOT written — when a row cannot be written or a concurrent pass created it first. Step (d) confirms each written share's row (marker dropped); a principal that already held the mirror is `CoveredByExisting` only when NO live row says the share was passed on (the rows are re-read after the shares were read). A recorded share that never landed is written again (never read as a removal); the reverse rule ends such a row with nothing removed (`assignment-ended`). **Race, both halves:** `AssignedAccessStore.CreateInheritedLedgerAsync` answers `null` on a 409/412 whose row reads back and writes NOTHING over it (before: it UPDATED the winner's row with the loser's write); every caller treats `null` as "not recorded" (pass incomplete, retried). **The same misclassification by a second path, closed:** a share kept as the record's last reader (S5) ended its row, so a later re-share recorded the parent's share as direct — its row now stays live (`Shared`, `kept-last-reader`), is retried each pass and removed once someone else can open the record (§6 xxii). The verifier's probe is now the regression test `AProvenanceWriteFault_NeverLeavesAShareThatLooksDirect_SoTheParentsUnshareStillRemovesIt`; the race is pinned at both layers (`ARowAConcurrentPassRecordsFirst_…`, `ADirectAccessRecordThatLosesTheRace_…`, and the store's `AnInheritedRowCreate_ThatLosesTheRaceToTheKey_…`). Seeds V01–V12 |
| 2 | LOW-MEDIUM: the delete READ-BACK in `CompleteIsolatedCreateAsync` was unpinned (V18) | **Pinned.** `SecureChildShareWorld.DeletesIgnored` (a delete that answers success while the row survives — the "accepted, not applied" shape task 133 models for revokes). `ChatCreate_WhenTheCompensationDeleteAnswersSuccessButTheRowSurvives_SaysSo_NeverNotCreated` and its Office twin: the row is named as existing (never "removed again / NOT created"), stays secure and team-owned, and the job later shares it. Seed V15 (= the verifier's V18, `removed = true`) bites both |
| 3 | LOW-MEDIUM: the mask comparison in `StillJustifiedByParentsAsync` was unpinned (W13) — a parent carrying LESS counted as full justification (over-retention) | **Pinned and completed.** `UnsharingFromTheMatter_LeavesExactlyWhatAnotherParentPassesOnAtALowerLevel`: re-filed from the matter (Collaborate) to a project sharing View Only — the matter's unshare takes its share back and the colleague ends with EXACTLY the project's View Only, in the same call. That last part is new: the `/unshare-user` fan-out now gives back what the record's current parents still pass on (the mirror's own pass, `PassUnshareOnAsync` → `SecureIfFiledUnderSecureCoreAsync`, sharee-only), so the person is never left without the remaining parent's share until the job (R3/R4); a give-back that fails or cannot be decided is reported (`InheritedUnsharePass.NotRegiven` → `filedRecordsNotUpdated`, its own sentence in the unshare copy). Seeds V16 (any comparison), **V16b** (= W13 exactly: absence still refuses, a lower mask justifies), V17–V21 |
| 4 | LOW: `declinedRows.Remove(row.Id)` in step (b) "has no observable effect" (W03) | **Proven wrong and pinned.** The statement is live for a row whose parent the record was RE-FILED AWAY from: the decline is from the old parent, the mirror runs over the NEW parents, so without the removal the new parent's share waits a whole job cycle. `TheJob_EndsADeclineFromAParentTheRecordWasReFiledAwayFrom_AndGivesTheNewParentsShareInTheSameRun`. Seed V22 bites |
| 5 | LOW: `ScanLedgerRootsAsync`'s in-memory `.Where(IsAssignedToScanRow)` duplicated the OData filter; the store's OData strings were exercised only by the live gate (W28) | **Fixed both halves.** The duplicate is removed (the OData filter is the one statement of the predicate; the fake keeps its own emulation). New `tests/integration/auth/UnifiedAccessControl/AssignedAccessStoreODataTests.cs` drives the PRODUCTION store over an in-memory `sprk_assignedaccesses` table behind `DataverseWebApiClient`'s virtual methods that EVALUATES the `$filter` it is sent as Dataverse does (case-insensitive; SQL three-valued logic — a comparison or `startswith` against an empty column is unknown, which is exactly why the scan says `eq null or not startswith(…)`), projects the `$select`, and answers 412 on a duplicate `sprk_ledgerkey` — the precedent is 142's `GrantTable`, which interprets the production filter. Six tests: the scan (inherited rows out in any casing; a null-source row kept; Revoked / deactivated out), the inherited read of a record (the team subject read through `_sprk_subjectteam_value`; malformed / other-record / deactivated rows out), the by-parent read, the race (412 → `null`, nothing written), a 412 with no row → throws, the create payload (team bind, key, mask). Seeds V23–V29 |
| 6 | LOW: publish size not measured | **Measured** (CLAUDE.md §10 item 4, the full convention): `dotnet publish -c Release` (the project's framework-dependent linux-x64), `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**, each from a FRESH detached short-path worktree (`C:\wt158m`, `C:\wt158a`, `C:\wt158r`, `C:\wt158h`), 212 files on every side, no MSB3030: `origin/master` `293fcd4c8` **45.65 MB**; task 158's own base `c8a964cb6` **45.94 MB**; this round's base `25b7ac3dd` **46.12 MB**; this round's head **46.13 MB**. Task 158 in all (incl. the 142-r6 ledger it merged) **+0.19 MB**; this round **+0.01 MB** (5,545 bytes); the UAC-r2 lane vs master **+0.47 MB** — far from the ≥+5 MB escalation and the 60 MB ceiling. The four worktrees were removed afterwards. `dotnet list package --vulnerable --include-transitive`: none (no package or project file changed) |
| 7 | LOW: the stranded-create copy promised a self-heal that may not come (provisioning refused the creator — e.g. walled between the plan and the provisioning) | **Fixed.** `SecureRootInheritResult.CompletesAutomatically` (`false` when provisioning REFUSED: every job run refuses again). The chat create error, the Office warning, the Office removed-refusal and the chat re-file error promise a retry only when it can come; after a refusal they say an administrator must review it (the Office removed copy names the refusal instead of "try again in a few minutes"); the CRITICAL log says the same. The create paths' "secured but incomplete" copy keeps its promise — every provisioning refusal comes BEFORE the creator's share, so a record shared to its creator can only be left incomplete by a fault (checked: no 4xx after the share step). Tests: chat / Office (×2) / re-file, each branch both ways (the fault cases now assert the promise; the refusal cases its absence). Seeds V30–V35 |
| 8–13 | VERIFIED items (CRITICAL item 5 fix, round 31 items 1/2, round 30 main paths, items 7–13 of r1c) | Unchanged in substance; every test of those classes still passes (the provenance tests now run over write-ahead rows) |
| 14 | The verifier's suites | Re-run here in full (below) |
| 15 | Round 30 not met after a provenance fault / race | Met: item 1 |
| 16 | AC8 not met (V18, W13 unbitten) | Met: items 2, 3 (and 4, 5); this round's 36 seeds below, all bitten |
| 17 | AC9 manual live gate | Pending for the main session, as expected — §10, now with the write-ahead expectations below |

### 15.1 Found while fixing (not in the verifier's list)

**Fixed here:**
- **A share task 143's enforcer removed was recorded as an operator's removal.** Step (a) read any vanished inherited
  share as `Declined` ("never re-added while the parent's share persists"), so a colleague walled off the filed record
  and later un-walled stayed without the parent's share for good. Task 142's criterion 9 already settles the same case on
  the same ledger (the enforcer's removal is a known cause, restored once lifted). Now: the person on the record's own No
  Access list → `Skipped` / `removed-by-no-access`, passed on again once the wall is lifted; a list that cannot be checked
  decides nothing (reported). `AShareTheNoAccessEnforcerRemoved_IsPassedOnAgainOnceTheWallIsLifted_…`,
  `AnInheritedShareRemovedWhileTheNoAccessListCannotBeChecked_…`; seeds V13, V14 (§6 xxiii).
- **A raise over an operator's own (Adopted) share went unrecorded** and was kept for good after the parent's unshare
  (`KeptAdopted`). Write-ahead records it, the operator's level as the level raised from; the unshare takes back only the
  raise. `ARaiseOverAnOperatorsOwnShare_IsRecorded_…`; seed V08 (§6 xxiv).

**Not closed here — escalated with the complete fix (they cross into task 142's invariant owner):**

- **E-158-v1-1 — the Assigned-To rule and the inheritance count each other's OBSERVATIONS as justification.** On a filed
  secure work assignment where the colleague holds the matter's inherited share, task 142's materializer finds that share
  covering its Collaborate target and records `CoveredByExisting` (it writes nothing). The matter's unshare then reaches
  `EndInheritedSourceAsync`, which keeps the share as "also direct" because an Assigned-To row names the user (any of
  Shared / Adopted / CoveredByExisting) → `KeptOtherField`. When the assignment later ends, 142 ends its covered row
  without touching the share ("nothing of ours to remove"). The colleague keeps the matter's level on the filed record
  with neither the matter nor the assignment justifying it — an over-retention round 30 forbids. The reverse direction
  is a transient under-grant: 142 removes its own unmodified share while a 158 row recorded `CoveredByExisting` over it;
  the matter's mirror re-grants at the next pass (≤ 5 min) — R3/R4 say minutes, but "immediate on save".
  **Complete fix (one change on each side, plus a reason value):** (1) 158's reverse rule counts an Assigned-To row as
  independent justification only when 142 WROTE or an operator ADOPTED the access (`Shared` / `Adopted`), never
  `CoveredByExisting`; (2) when 158 then removes the share, each 142 `CoveredByExisting` row naming that user whose
  coverage is gone is set `Skipped` with a new reason `covering-share-ended` (a known cause — never 142's
  `removed-out-of-band` → `Declined`) and 142's materializer runs for the record at once (L1), so on a secure record the
  assignee is SUGGESTED (owner A3) rather than silently dropped; (3) symmetrically, 142's `EndAssignmentAsync`, after it
  removes its own share on a filed secure root, calls the inheritance's sharee-only pass for that record (the parent's
  mirror re-given at once, provenance via write-ahead). Tests: both directions over the two real rules; seeds per change.
  Why not done here: (2) and (3) change task 142's materializer — its known-cause set and its end-of-assignment path — and
  add a ledger reason that rule must honour; that is a decision for the owner of both rules (the main session), not a
  silent edit from 158.
- **E-158-v1-2 — an operator's `/unshare-user` on a filed record can be undone by a pass running at the same moment.**
  The route revokes the share and THEN writes `Declined` (task 142's marker); a pass that read the row before the marker
  and the share after the revoke re-adds the share (now write-ahead: its `share-pending` update lands over nothing yet
  declined), and the marker then lands on a row whose share is back — the next pass reads `Declined` and leaves the
  share in place. The same window exists for task 142's own auto-shares. **Complete fix:** the operator routes write the
  marker BEFORE the revoke (write-ahead, the shape of this round's fix), and every ledger update a pass makes sends the
  row's ETag as `If-Match` from the read it decided on (the round-42 mechanism), so a marker that lands between a pass's
  read and its write fails that write (412) and the pass re-decides. Needs `If-Match` on `DataverseWebApiClient`
  updates (task 140 is adding it for grant rows on its own branch — round 42) and the marker reorder in
  `InternalShareEndpoints` (task 142's code): a cross-task change for the integration lane.

### 15.2 Tests (KEEP paths, ADR-038)

New this round (29 methods): `SecureRootInheritanceRound31Tests` +16 (write-ahead ×5, race ×2, last reader ×2, Adopted
raise, item 3 ×3, item 4, walls ×2 — 57 cases in the class), `SecureRootInheritanceWriterTests` +7 (items 2 ×2, 7 ×5 — and
three existing tests now assert the promise they make on a fault), `AssignedAccessStoreODataTests` 6 (new class, in
`tests/integration/auth/UnifiedAccessControl`, 142's KEEP path). Test doubles: `FakeAssignedAccessStore` emulates the
production conflict path (answers `null`, never throws on a duplicate) with a `BeforeInheritedCreate` hook (a concurrent
pass) and `FailLedgerUpdates`; `SeamNoAccessListReader.Lift`; `SecureChildShareWorld.DeletesIgnored`. No banned pattern
(no HTTP double: the Web API double overrides the client's virtual methods, as 142's `GrantTable` does).

| Suite (this worktree, once, at the end, on `651cf79b3` — later commits change docs only) | Result |
|---|---|
| The 158 classes + the store test + 142's ledger / job / marker classes + the share / mirror classes (the seed filter) | **508 / 508** |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | **Passed 15353 / Failed 0 / Skipped 54** (Total 15407, 20 m 18 s) — r1c's 15324 + the 29 new |
| NetArchTest (`tests/Spaarke.ArchTests`) | **373 / 373** |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | **104 / 104** |
| `tests/integration/Spe.Integration.Tests` | **403 passed / 25 skipped / 0 failed** (Total 428) |

No contention failures this time (the suites ran sequentially, after the seed batch). All four projects build with analyzers
on: 0 errors (the 5 warnings in `Spe.Integration.Tests` are in that project's own existing files).

### 15.3 Seeds (harness `seeds158v1.py`: one guard removed per run with a runtime-false condition — or the pre-fix behaviour restored — build, the 158 classes + the store test + 142's job/marker classes + the share/mirror classes (508 tests); the file restored byte-identical from memory and touched; `git status -- src` clean after the batch)

| Seed | Guard removed | Result | Red (first; +n more) | Restored |
|---|---|---|---|---|
| V01 | item 1: a share is written although its record could not be written | bit | `AProvenanceWriteFault_NeverLeavesAShareThatLooksDirect_SoTheParentsUnshareStillRemovesIt` (+1) | byte-identical |
| V02 | item 1: no write-ahead record (the share is written first, as before r1c-v1) | bit | `AConfirmationFault_LeavesTheShareOnRecordAsPassedOn_SoTheNextRunConfirmsIt_AndTheUnshareRemovesIt` (+33) | byte-identical |
| V03 | item 1: a recorded share that never landed is read as a removal | bit | `ARecordedShareThatNeverLanded_IsWrittenAgainByTheNextRun_NeverReadAsARemoval` (+2) | byte-identical |
| V04 | item 1: the reverse rule reads a never-landed share as modified | bit | `UnsharingFromTheMatter_EndsARecordedShareThatNeverLanded_RemovingNothing` | byte-identical |
| V05 | item 1 (S5 path): a kept last reader shared again is not confirmed | bit | `AShareKeptAsTheLastReader_ThatTheMatterSharesAgain_IsStillPassedOn_NeverRecordedAsDirect` | byte-identical |
| V06 | item 1 (S5 path): the last reader's row is ended (as before r1c-v1) | bit | `AShareKeptAsTheLastReader_IsRemovedOnceSomeoneElseCanOpenTheRecord` (+1) | byte-identical |
| V07 | item 1: the prior level is not read after the write-ahead marker | bit | `ARaiseOverAnOperatorsOwnShare_IsRecorded_SoTheParentsUnshareTakesBackOnlyTheRaise` (+1) | byte-identical |
| V08 | item 1: a raise over an operator's own (Adopted) share goes unrecorded | bit | `ARaiseOverAnOperatorsOwnShare_IsRecorded_SoTheParentsUnshareTakesBackOnlyTheRaise` | byte-identical |
| V09 | item 1 (race): a write-ahead record that lost the race still lets the share be written | bit | `ARowAConcurrentPassRecordsFirst_IsNeverOverwritten_AndNoShareIsWrittenOnTheStaleDecision` | byte-identical |
| V10 | item 1 (race): a covered-by-existing record that lost the race is not reported | bit | `ADirectAccessRecordThatLosesTheRace_NeverOverwritesTheShareAConcurrentPassRecordedAsPassedOn` | byte-identical |
| V11 | item 1 (race): the store's conflict path writes over the winner's row (as before r1c-v1) | bit | `AnInheritedRowCreate_ThatLosesTheRaceToTheKey_AnswersNull_AndWritesNothingOverTheRowThatWon` | byte-identical |
| V12 | item 1: a 412 with no row behind it is read as a lost race | bit | `AnInheritedRowCreate_RefusedWithoutARowToReadBack_Throws` | byte-identical |
| V13 | a share the No Access enforcer removed is recorded Declined | bit | `AShareTheNoAccessEnforcerRemoved_IsPassedOnAgainOnceTheWallIsLifted_NeverReadAsAnOperatorsRemoval` | byte-identical |
| V14 | an unverifiable No Access list decides the removal anyway | bit | `AnInheritedShareRemovedWhileTheNoAccessListCannotBeChecked_IsNeitherReAddedNorRecorded_AndTheRunFails` | byte-identical |
| V15 | item 2 (verifier V18): the delete is not read back | bit | `ChatCreate_WhenTheCompensationDeleteAnswersSuccessButTheRowSurvives_SaysSo_NeverNotCreated` (+1) | byte-identical |
| V16 | item 3 (verifier W13): a parent carrying LESS counts as justifying the whole share | bit | `AConfirmationFault_LeavesTheShareOnRecordAsPassedOn_SoTheNextRunConfirmsIt_AndTheUnshareRemovesIt` (+19) | byte-identical |
| V16b | item 3 (verifier W13, exact): only a parent that does not carry the person at all counts as not justifying | bit | `UnsharingFromTheMatter_LeavesExactlyWhatAnotherParentPassesOnAtALowerLevel` (+2) | byte-identical |
| V17 | item 3: what the remaining parents pass on is not given back | bit | `UnsharingFromTheMatter_LeavesExactlyWhatAnotherParentPassesOnAtALowerLevel` (+2) | byte-identical |
| V18 | item 3: a give-back whose write failed is not reported | bit | `UnsharingFromTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt` | byte-identical |
| V19 | item 3: a give-back that could not be decided is not reported | bit | `UnsharingFromTheMatter_WhenWhatTheRemainingParentsPassOnCannotBeDecided_ReportsIt` | byte-identical |
| V20 | item 3: the unshare route does not count records not given back | bit | `UnsharingFromTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt` (+1) | byte-identical |
| V21 | item 3: a give-back failure counted as a share not removed | bit | `UnsharingFromTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt` (+1) | byte-identical |
| V22 | item 4 (verifier W03): an ended decline still withholds the principal in the same pass | bit | `TheJob_EndsADeclineFromAParentTheRecordWasReFiledAwayFrom_AndGivesTheNewParentsShareInTheSameRun` | byte-identical |
| V23 | item 5: the Assigned-To scan counts inherited rows | bit | `TheAssignedToScan_LeavesInheritedShareRowsOut_AndKeepsAnAssignedToRowWithOrWithoutASourceField` | byte-identical |
| V24 | item 5: the Assigned-To scan drops rows with no source field | bit | `TheAssignedToScan_LeavesInheritedShareRowsOut_AndKeepsAnAssignedToRowWithOrWithoutASourceField` | byte-identical |
| V25 | item 5: the inherited read does not select the team | bit | `AnInheritedRowCreate_BindsTheRecordAndATeamSubject_AndReadsBackAsThatTeamWithTheMaskWritten` (+2) | byte-identical |
| V26 | item 5: a malformed inherited row is returned | bit | `TheInheritedLedgerOfARecord_IsItsInheritedRowsOnly_WithTheTeamTheyWerePassedOnTo` | byte-identical |
| V27 | item 5: the by-parent read returns other parents' rows | bit | `TheInheritedLedgerOfAParent_IsEveryLiveRowPassedOnFromIt_OnAnyRecord` | byte-identical |
| V28 | item 5: the by-parent read returns deactivated rows | bit | `TheInheritedLedgerOfAParent_IsEveryLiveRowPassedOnFromIt_OnAnyRecord` | byte-identical |
| V29 | item 5: a team principal is bound as a user | bit | `AnInheritedRowCreate_BindsTheRecordAndATeamSubject_AndReadsBackAsThatTeamWithTheMaskWritten` | byte-identical |
| V30 | item 7: a refusal still promises a self-heal | bit | `ChatCreate_WhenTheCreatorIsWalledOffAfterThePlan_AndTheRowCannotBeRemoved_PromisesNoSelfHeal` (+3) | byte-identical |
| V31 | item 7: a fault no longer promises the retry it gets | bit | `ChatCreate_WhenTheCreatorCannotBeSharedNorTheRowRemoved_SaysSo_AndTheJobSharesItToTheCreator` (+3) | byte-identical |
| V32 | item 7: chat's stranded copy ignores the refusal | bit | `ChatCreate_WhenTheCreatorIsWalledOffAfterThePlan_AndTheRowCannotBeRemoved_PromisesNoSelfHeal` | byte-identical |
| V33 | item 7: Office's stranded warning ignores the refusal | bit | `OfficeCreate_WhenTheMakerIsWalledOffAfterThePlan_AndTheProjectCannotBeRemoved_PromisesNoSelfHeal` | byte-identical |
| V34 | item 7: Office's removed copy suggests a retry after a refusal | bit | `OfficeCreate_WhenTheMakerIsWalledOffAfterThePlan_TheProjectIsRemoved_AndNoRetryIsSuggested` | byte-identical |
| V35 | item 7: the re-file copy promises a retry after a refusal | bit | `ChatUpdate_WhenTheCreatorIsWalledOffAfterThePreCheck_PromisesNoAutomaticRetry` | byte-identical |

**36 seeds, 36 bit, 0 not bitten; every file restored byte-identical and touched; `git status -- src` clean after the batch.**

### 15.4 Live gates (main session) — what changes

Gate 158-0 (the ledger schema, BEFORE the deploy) is unchanged: write-ahead needs no new column or option (the marker is a
`sprk_reason` value on a `Shared` row). Gate 158-a, additionally: each inherited-share row on the new work assignment
reads `sprk_state` 100000001 with `sprk_reason` **empty** (confirmed) — a row still reading `share-pending` after the
job's next run means a share whose write never landed (the run reports it). Gate 158-b unchanged.

## 16. Fix round r1c-v2 (the re-verification of `task/uac-r2-158-r1c-v1`; main-session rounds 39 and 47) — item by item

Branch `task/uac-r2-158-r1c-v2` from `task/uac-r2-158-r1c-v1` (`f090b1221`). Binding: rounds 30 and 31, owner round 32,
**round 39** (task 158, from its second verification) and **round 47** (task 158, from the re-verification), round 3 S5 / A4,
the standing directive (round 15). No owner question was needed; the readings this round adds are §16.6 xxvi–xxxii
(owner-reversible).

**Correction of r1c-v1's own claims.** §15 cited round 39 as binding but its code predated it — round 39 was NOT
implemented (round 47 item 0). §15.3 said "36 seeds, 36 bit … no unpinned guard remains": the verifier's own seeds X03,
X07, X09 and X17 (access-affecting) and X08, X15 (cosmetic) bit nothing. Both are closed below, and the seed table of this
round (§16.3) covers every branch this round adds.

| # | Verifier item | Outcome |
|---|---|---|
| 1 (1) | BLOCKING — round 39 item 1 not implemented: the code and a committed test did the reverse; §6 xiii and the I-11 row contradicted it | **Implemented.** `/unsecure-project` **Step 4.5** (after Step 4 revokes the matter's / project's shares, BEFORE the flag is cleared): `SecureRootInheritance.EndWhatAParentPassedOnAsync` runs round 30's reverse rule (`EndInheritedSourceAsync`) on every live row the record passed on, the record justifying NOTHING though its flag still reads secure (`endingParent` → `StillJustifiedByParentsAsync` skips it). Ended: the unmodified inherited share. Kept: a direct share, a raised mask (put back), a share another secure parent still justifies, the last reader (S5). Not complete → 500 `children_incomplete` (`filedRecordsNotUpdated`), the flag KEPT, the same call completes it. **Step 5.5** (after the flag): what the filed records' other secure parents still pass on is given back (`GiveBackAsync`) — reported when it cannot be. The **already-ordinary** path ends what is still on record and gives back the rest. Wherever the reverse rule looks (step (b) of every pass, `/unshare-user`), a parent that reads NOT secure — or no longer exists — passes nothing on (`ParentMirrorAnswer.PassesNothingOn`); flagged-but-not-isolated holds; an EMPTY flag is unreadable. The committed test `UnsharingFromAMatterThatIsNoLongerSecure_EndsNothingItPassedOn` is replaced by `UnsecuringTheMatter_EndsWhatItPassedOn_AndTheFiledRecordStaysSecure`; two r1 tests that unsecured the matter to reach a filed record's own unsecure now RE-FILE it away instead (they test Step 3.6, which round 39 leaves as it was). §6 xiii marked REVERSED; the I-11 row rewritten (and the stale I-2 remark "created as an ordinary row" corrected). Tests per kept / ended case and per fault: §16.2 |
| 1 (2) | BLOCKING — round 39 item 2 not implemented: `/share-user` and the provisioning colleague check asked the record's own list only | **Implemented — ONE guard entry point.** `SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync` (two overloads: the record's CURRENT filing, read through the ONE parent walk `SecureRootInheritance.ReadSecureParentsAsync` — now static, the inheritance's own decision; or a filing the caller supplies, for a write that has not happened yet) asks the record's own list (`SecureWallRecordScope.AsFlagged` / `BeingSecured` / `Prospective`) AND every secure parent's (as the secure records they are); an unreadable filing or parent is `Unverifiable`; a wall anywhere wins over an unverifiable answer; `ParentTable` names whose list refused and `FilingUnreadable` that the filing itself could not be read (the message, never the entry). Every such decision goes through it: `/share-user` (same codes; message names the parent's list), `/provision-project` colleagues (same per-person codes), the Assigned-To suggestion and restore on a secure record (round 39's "every share"), and the creator checks round 31 item 1 had as three loops (`CheckCreatorWallsAsync`, the re-file pre-check, the create plan) — no second copy of the walk or of the per-parent loop remains |
| 2 | AC8 overstated: X03, X07, X09, X17 (access-affecting) and X08, X15, X16 survived | **Closed.** Each has a test that bites (seeds Y39–Y43, Y18): X09 `ReRaisingAnInheritedShare_KeepsWhatItFirstRaisedFrom_SoTheParentsUnshareRemovesItAll` (the verifier's probe: View → Collaborate → unshare leaves 0, seeded 1); X03 `ADirectShareBesideAnotherParentsShareThatNeverLanded_IsRecordedAsDirect_NeverAsPassedOn`; X07 `UnsharingFromTheMatter_WhenTheGiveBackCannotCheckTheNoAccessList_ReportsIt`; X17 `AnOperatorsAdoptedRow_IsNeverRewrittenAsPassedOn_ByAPassThatFindsTheShareCovered`; X08 `UnsharingFromTheMatter_KeepsAShareSomeoneElseGaveWhileTheRecordedOneNeverLanded`; X15 — the give-back sentence asserted in `UnsharingFromTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt`; **X16** (`RowEnded` on a kept last reader — "close to equivalent") — NOT equivalent: in the pass that keeps the colleague as the last reader for the matter, a project the record was re-filed under can raise the colleague, and the raise's level-raised-from is read from the kept row; marking it ended in memory made the raise start from the matter's share (a direct-looking level). Pinned by `AShareKeptAsTheLastReader_IsNeverTheLevelAnotherParentsRaiseStartsFrom` (seed Y52) |
| 3 | LOW — the Office removed-refusal appended provisioning's ProblemDetails (doubled period, "Nothing was changed.") | **Fixed** (round 47 item 4): one sentence of its own naming the refusal code and that an administrator must review. The same defect class in the chat create / re-file errors (`(code: detail)` with provisioning's sentence inside) is fixed too: the code only. Pinned by the writer test (no `..`, no "Nothing was changed", the administrator sentence) and seed Y51 |
| 4–6 | VERIFIED (r1c's CRITICAL fix, round 31 items, write-ahead core, the delete read-back, the give-back, the lost race, CompletesAutomatically; the POML parses; owner round 32 recorded; no live write; worktrees gone; E-158-v1-1/-2 confirmed in code; the verifier's suites) | Unchanged in substance; every test of those classes still passes (§16.2). The round-31 creator paths now reach the guard's one entry point — the same decisions, re-pinned by seeds Y25, Y26, Y30, Y31 |
| 7 | Round 39 not met | **Met** — item 1 above |
| 8 | AC8 not met for r1c-v1 | **Met** — item 2 above; this round's seeds §16.3 |
| 9 | Round 30 not fully met while E-158-v1-1 is open | **Met — round 47 item 1, all three parts.** (1) The reverse rule counts an Assigned-To row as justification ONLY when `Shared` or `Adopted`, never `CoveredByExisting`. (2) When the rule then removes or narrows the share, each 142 `CoveredByExisting` row naming that user is set `Skipped` / `covering-share-ended` (new reason value, `AssignedAccessReason.CoveringShareEnded` — a known cause) AHEAD of the removal, and 142's materializer runs at once — on the secure record the assignee is SUGGESTED (PendingConfirmation). (3) 142's materializer, after an assignment ended on a work assignment or project, runs the inheritance's sharee-only pass for it (`PassShareesToFiledRecordAsync`, reached through a scope). Tests: `UnsharingFromTheMatter_TakesBackAShareTheAssignedToRuleOnlyFoundCovering_AndSuggestsTheAssigneeAtOnce`, `TheCoveringShareEndedMarker_IsWrittenBeforeTheShareIsNarrowed`, `AnAssignmentThatEndsOnAFiledSecureRecord_GivesItsParentsShareesAtOnce`, `AnAssignmentThatEndsWhereNoHostIsReachable_LeavesTheParentsShareesToTheJob`; seeds Y32–Y37 |
| 10 | AC9 manual live gate | Pending for the main session, as expected (§16.5) |
| R47-2 | Round 47 item 2 (E-158-v1-2) | **Now (this lane): done** — `/unshare-user` writes the operator's Declined marker BEFORE the revoke (`AnOperatorsUnshareOnTheFiledRecord_IsRecordedDeclinedBeforeTheShareIsRemoved`, seed Y38). **At integration, after task 140 merges: the If-Match half** — round 47 places it there (task 140's `DataverseWebApiClient` support, round 42, is not on this branch); the exact checklist item, tests and seeds are in §13 |

### 16.1 Found while fixing (not in the verifier's list) — fixed here

- **The race round 39's Step 4.5 would have opened (and the one round 30's `/unshare-user` fan-out already had).** A pass
  that read a parent as isolated and sharing a person, then wrote that person's share, could land its write AFTER a
  concurrent unsecure's (or unshare's) reverse pass had read the provenance — before the share's row existed, or with the row
  still unconfirmed and the share not yet in place, so the reverse pass ended the row and removed nothing. The share then
  outlived its source; a later pass would even record it as direct access. **Fix — (e), the post-write check:** after (d),
  each share the pass WROTE is decided again from fresh reads: a row a concurrent pass ended while the share is in place is
  put back on record (`ReopenAsync`; a create that loses the alternate-key race is reported); a row ended with its share
  removed stays ended; and a share whose parent no longer passes it on — unsecured, re-owned out of isolation, or no longer
  sharing the person — is ended by the reverse rule at once; a parent that cannot be read is reported. Ordering argument: the
  check reads after the share landed, so either the concurrent reverse pass read the row (and ended it — the check then puts
  it back and decides it) or the check sees the parent already changed. Tests: `AShareWrittenWhileItsParentIsUnsecured_IsEndedOnceItLands`,
  `AShareWhoseRowAConcurrentUnshareEnded_IsPutBackOnRecordAndEnded`, `AShareWhoseRowWasEndedWhileTheParentStillSharesIt_IsPutBackOnRecordAsPassedOn`,
  `AShareAConcurrentPassAlsoRemoved_IsNotPutBackOnRecord`, `AShareWhoseRowWasDeletedAsItLanded_IsPutBackOnRecord`,
  `PuttingAShareBackOnRecord_ThatLosesTheRace_IsReported`, `AShareWhoseParentCannotBeReadOnceItLands_IsReported`; seeds Y44–Y50.
- **A parent that no longer exists** (deleted) was read as "not isolated — ends nothing", so what it passed on stayed for
  good. It passes nothing on (xxvi).
- **`ExternalAccessContractTests`' host** used the production `IGenericEntityService` (no Dataverse there), so the guard's new
  filing walk failed closed and `/share-user` answered 500: its guard is registered over a no-rows filing world.
- **A truncated provenance read** on the reverse fan-out (pre-existing in `PassUnshareOnAsync`, now shared with the unsecure)
  had no test: pinned for both routes (seed Y16).
- **Whose list an unreadable FILING refusal names.** When what a record is filed under cannot be read, `/share-user`,
  the `/provision-project` colleague warning and provisioning's creator refusal (forward and resume) answered with the
  right retryable code but named "the No Access list for this record" — not the list that could not be checked. The
  guard's entry point now marks that answer `SecureShareWallDecision.FilingUnreadable` and every caller names "a secure
  record this record (it) is filed under". Same codes and statuses. Tests: `SharingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefused`,
  `ProvisioningAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefusedNamingThatList`,
  `ResumingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefusedNamingThatList`,
  `ProvisioningAFiledRecord_WhenTheMatterBecomesUnreadableBeforeTheColleagues_SkipsThemNamingThatList`; seeds Y53–Y58.

### 16.2 Tests (KEEP paths, ADR-038)

New class `tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceRound39Tests.cs` — 47 test methods,
driven through the real routes (`/unsecure-project`, `/provision-project`), the real share / unshare handlers, the real
inheritance, job, synchronizer, No Access guard (the host's, over its deny list and world) and the real Assigned-To
materializer over the fixture's shares and ledger. Round 31 tests: 3 rewritten (above), X15's sentence asserted; the
writer test asserts the copy. Test doubles (boundary only): the provisioning fixture gains `OnRevoke` / `OnModify` /
`OnGranted` hooks on its recording share seam, a per-test `AssignedAccess` harness over the same ledger whose materializer
the host's inheritance holds; the fake ledger gains `BeforeInheritedRead` and `InheritedByParentTruncated`; the harness
gains `Entities` / `GuardOverride` / `SharesOverride` / `Scopes` and `NoFilingRows()`. No banned pattern (no HTTP double, no
DI-registration or null-check test).

| Suite (once, at the end, on the code head `823aebf0f`) | Result |
|---|---|
| Affected filter (§16.3's 1046 tests) | Passed 1046 / Failed 0 |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | Passed 15399 / **Failed 1** / Skipped 54 (Total 15454, 24 m 44 s, the machine shared with other sessions' test runs). The one failure — `PinnedMemoryEndpointsContractTests.DeletePin_Authenticated_Returns204AndEmitsCounter` [40 s], memory pins, untouched by this round — passes on an isolated re-run of its class (15 / 15): contention. r1c-v1's 15407 passed + the 47 new = 15454 total |
| NetArchTest (`tests/Spaarke.ArchTests`) | Passed 373 / Failed 0 |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (in full) | Passed 104 / Failed 0 / Skipped 0 (Total 104) |
| `tests/integration/Spe.Integration.Tests` (in full) | Passed 403 / Failed 0 / Skipped 25 (Total 428) |

On the intermediate head `a4212a91d` (before the `FilingUnreadable` change): full unit Passed 15397 / Failed 0 / Skipped 54
(Total 15451); NetArchTest 373 / 373; `Sprk.Bff.Api.IntegrationTests` 104 / 104; `Spe.Integration.Tests` 403 passed / 25
skipped / 0 failed.

### 16.3 Seeds (harness `seeds158v2.py`: one guard removed per run — a runtime-false condition, or the pre-fix behaviour restored — build, the affected filter (1046 tests: the 158 classes + 142's ledger / materializer / marker / job classes + the share / mirror / guard / provisioning / unsecure / contract / writer classes); the file restored byte-identical and touched; `git status -- src` clean after the batch)

| Seed | Guard removed | Result | Red (first; +n more) | Restored |
|---|---|---|---|---|
| Y01 | round 39: a parent that no longer exists is read as flagged-not-isolated (ends nothing) | bit | `TheJob_EndsWhatADeletedParentPassedOn` | byte-identical |
| Y02 | round 39: an unsecured parent (flag false) is read as flagged-not-isolated (ends nothing — xiii) | bit | `TheJob_EndsWhatAnUnsecuredParentStillPassedOn` (+1) | byte-identical |
| Y03 | a parent flagged secure but not isolated is read as unsecured (ends what it passed on) | bit | `TheJob_HoldsWhatAParentFlaggedSecureButNotIsolatedPassedOn` | byte-identical |
| Y04 | round 17 item 3: an EMPTY parent flag is read as unsecured | bit | `TheJob_NeverReadsAnEmptyParentFlagAsUnsecured_ItReportsTheRecord` | byte-identical |
| Y05 | round 39: step (b) ignores that a parent passes nothing on | bit | `TheJob_EndsWhatAnUnsecuredParentStillPassedOn` (+1) | byte-identical |
| Y06 | round 39: /unshare-user on an unsecured parent ends nothing | bit | `UnsharingFromAnUnsecuredMatter_EndsWhatItStillPassedOn` | byte-identical |
| Y07 | round 39: the parent being unsecured still counts as justifying (its flag still reads secure) | bit | `UnsecuringTheMatter_KeepsAShareAnotherSecureParentStillPassesOn` | byte-identical |
| Y08 | round 39: the unsecure's Step 4.5 does not run | bit | `UnsecuringTheMatter_EndsWhatItPassedOn_AndTheFiledRecordStaysSecure` (+10) | byte-identical |
| Y09 | round 39: a Step 4.5 that did not complete still clears the flag | bit | `UnsecuringTheMatter_WhenWhatItPassedOnCannotBeRead_StopsBeforeTheFlag` (+2) | byte-identical |
| Y10 | round 39: Step 5.5 gives nothing back | bit | `UnsecuringTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt_AndTheJobGivesIt` (+1) | byte-identical |
| Y11 | round 39: a give-back that failed after the flag is not reported | bit | `UnsecuringTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt_AndTheJobGivesIt` | byte-identical |
| Y12 | round 39: the already-ordinary path ends nothing left over | bit | `UnsecuringAMatterThatIsAlreadyOrdinary_GivesBackWhatAnotherParentPassesOn` (+2) | byte-identical |
| Y13 | round 39: the already-ordinary path does not report what it could not end | bit | `UnsecuringAMatterThatIsAlreadyOrdinary_WhenWhatItStillPassedOnCannotBeRemoved_ReportsIt` | byte-identical |
| Y14 | round 39: the already-ordinary path gives nothing back | bit | `UnsecuringAMatterThatIsAlreadyOrdinary_GivesBackWhatAnotherParentPassesOn` | byte-identical |
| Y15 | round 39: an unreadable provenance is read as nothing passed on | bit | `UnsecuringTheMatter_WhenWhatItPassedOnCannotBeRead_StopsBeforeTheFlag` | byte-identical |
| Y16 | a truncated provenance read counts as complete | bit | `UnsecuringTheMatter_WhenWhatItPassedOnIsMoreThanOneReadReturns_StopsBeforeTheFlag` (+1) | byte-identical |
| Y17 | what other parents still pass on is never collected for the give-back | bit | `UnsecuringAMatterThatIsAlreadyOrdinary_GivesBackWhatAnotherParentPassesOn` (+6) | byte-identical |
| Y18 | X07: a HELD give-back is counted as given back | bit | `UnsharingFromTheMatter_WhenTheGiveBackCannotCheckTheNoAccessList_ReportsIt` | byte-identical |
| Y19 | a FAILED give-back is counted as given back | bit | `UnsecuringTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt_AndTheJobGivesIt` (+1) | byte-identical |
| Y20 | a give-back that could not be decided is counted as given back | bit | `UnsharingFromTheMatter_WhenWhatTheRemainingParentsPassOnCannotBeDecided_ReportsIt` | byte-identical |
| Y21 | round 39 item 2: an unreadable filing is not refused | bit | `SharingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefused` (+1) | byte-identical |
| Y22 | round 39 item 2: no secure parent's list is asked | bit | `ChatUpdate_WhenTheCreatorIsOnTheSecureMattersNoAccessList_IsRefusedBeforeThePatch` (+13) | byte-identical |
| Y23 | a wall is not final: an unverifiable list wins over a walled one | bit | `SharingAFiledRecord_WhenItsOwnListCannotBeCheckedButTheMattersWallsThePerson_IsRefusedAsWalled` | byte-identical |
| Y24 | the parent whose list decided is not named | bit | `ChatUpdate_WhenTheCreatorIsOnTheSecureMattersNoAccessList_IsRefusedBeforeThePatch` (+5) | byte-identical |
| Y25 | a record being secured is asked as flagged (the CRITICAL of the first verification) | bit | `ChatUpdate_WhenTheCreatorIsOnTheRecordsOwnNoAccessList_IsRefusedBeforeThePatch` (+2) | byte-identical |
| Y26 | a create's own prospective list (its organizations) is not asked | bit | `ChatCreate_WhenTheCallerIsWalledOffAnOrganizationTheRecordWouldReference_IsRefused_AndNothingIsCreated` | byte-identical |
| Y27 | round 39 item 2: /share-user asks the record's own list only (as before) | bit | `SharingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefused` (+2) | byte-identical |
| Y28 | round 39 item 2: provisioning's colleagues are asked about the record's own list only (as before) | bit | `ProvisioningAFiledRecord_SkipsANamedColleagueOnTheSecureMattersNoAccessList` | byte-identical |
| Y29 | round 39 item 2: the Assigned-To suggestion asks the record's own list only (as before) | bit | `TheAssignedToRule_NeverSuggestsAPersonOnTheSecureMattersNoAccessList` | byte-identical |
| Y30 | the re-file pre-check refuses nobody | bit | `ChatUpdate_WhenTheCreatorIsOnTheSecureMattersNoAccessList_IsRefusedBeforeThePatch` (+1) | byte-identical |
| Y31 | provisioning's creator check refuses nobody | bit | `OfficeCreate_WhenTheMakerIsWalledOffAfterThePlan_AndTheProjectCannotBeRemoved_PromisesNoSelfHeal` (+12) | byte-identical |
| Y32 | round 47 (1): an Assigned-To CoveredByExisting row still justifies keeping the share | bit | `TheCoveringShareEndedMarker_IsWrittenBeforeTheShareIsNarrowed` (+1) | byte-identical |
| Y33 | round 47 (2): the covering row is never marked (read later as removed out of band) | bit | `TheCoveringShareEndedMarker_IsWrittenBeforeTheShareIsNarrowed` (+1) | byte-identical |
| Y34 | round 47 (2): the marker is written AFTER the share is narrowed | bit | `TheCoveringShareEndedMarker_IsWrittenBeforeTheShareIsNarrowed` | byte-identical |
| Y35 | round 47 (2): the materializer is not run at once | bit | `UnsharingFromTheMatter_TakesBackAShareTheAssignedToRuleOnlyFoundCovering_AndSuggestsTheAssigneeAtOnce` | byte-identical |
| Y36 | round 47 (3): an ended assignment does not run the sharee-only pass | bit | `AnAssignmentThatEndsOnAFiledSecureRecord_GivesItsParentsShareesAtOnce` | byte-identical |
| Y37 | round 47 (3): a materializer without a host scope is not guarded | build failed (the runtime-false form did not compile) — re-seeded as Y37b | — | byte-identical |
| Y38 | round 47 item 2: the Declined marker is written AFTER the revoke (as before) | bit | `AnOperatorsUnshareOnTheFiledRecord_IsRecordedDeclinedBeforeTheShareIsRemoved` | byte-identical |
| Y39 | X09: a re-raise takes its own earlier mask as the level it raised from | bit | `ReRaisingAnInheritedShare_KeepsWhatItFirstRaisedFrom_SoTheParentsUnshareRemovesItAll` | byte-identical |
| Y40 | X03: another parent's row counts though its level is not on the record | bit | `ADirectShareBesideAnotherParentsShareThatNeverLanded_IsRecordedAsDirect_NeverAsPassedOn` | byte-identical |
| Y41 | X17: an operator's Adopted row may be rewritten by a pass that finds the share covered | bit | `AnOperatorsAdoptedRow_IsNeverRewrittenAsPassedOn_ByAPassThatFindsTheShareCovered` | byte-identical |
| Y42 | X08: a never-landed row whose share someone changed is ended as 'assignment ended' | bit | `UnsharingFromTheMatter_KeepsAShareSomeoneElseGaveWhileTheRecordedOneNeverLanded` | byte-identical |
| Y43 | X15: the unshare copy drops the give-back sentence | bit | `UnsharingFromTheMatter_WhenWhatAnotherParentPassesOnCannotBeGivenBack_ReportsIt` | byte-identical |
| Y44 | post-write: the shares a pass wrote are never decided again | bit | `PuttingAShareBackOnRecord_ThatLosesTheRace_IsReported` (+6) | byte-identical |
| Y45 | post-write: a row ended with its share is put back on record anyway | bit | `AShareAConcurrentPassAlsoRemoved_IsNotPutBackOnRecord` | byte-identical |
| Y46 | post-write: a row a concurrent pass ended is never put back on record | build failed (the runtime-false form did not compile) — re-seeded as Y46b | — | byte-identical |
| Y47 | post-write: losing the race to put a row back is not reported | bit | `PuttingAShareBackOnRecord_ThatLosesTheRace_IsReported` | byte-identical |
| Y48 | post-write: an unreadable parent is read as no longer passing it on | bit | `AShareWhoseParentCannotBeReadOnceItLands_IsReported` | byte-identical |
| Y49 | post-write: a share still passed on is ended anyway | bit | `UnsecuringTheMatter_KeepsAShareSomeoneChangedSince` (+51) | byte-identical |
| Y50 | post-write: a share whose parent passes nothing on is kept | bit | `AShareWrittenWhileItsParentIsUnsecured_IsEndedOnceItLands` (+1) | byte-identical |
| Y51 | round 47 item 4: the Office removed copy appends provisioning's detail (as before) | bit | `OfficeCreate_WhenTheMakerIsWalledOffAfterThePlan_TheProjectIsRemoved_AndNoRetryIsSuggested` | byte-identical |
| Y37b | round 47 (3): a materializer without a host scope is not guarded (Y37 re-seeded so it compiles) | bit | `AnAssignmentThatEndsWhereNoHostIsReachable_LeavesTheParentsShareesToTheJob` | byte-identical |
| Y46b | post-write: a row a concurrent pass ended is never put back on record (Y46 re-seeded so it compiles) | bit | `PuttingAShareBackOnRecord_ThatLosesTheRace_IsReported` (+3) | byte-identical |
| Y52 | X16: a kept last reader's row is marked ended in memory (the raise of another parent then starts from it) | bit | `AShareKeptAsTheLastReader_IsNeverTheLevelAnotherParentsRaiseStartsFrom` | byte-identical |
| Y53 | an unreadable filing is not told apart from the record's own list | bit | `SharingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefused` (+3) | byte-identical |
| Y54 | /share-user names the record's own list for an unreadable filing | bit | `SharingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefused` | byte-identical |
| Y55 | the provisioning colleague warning names the record's own list for an unreadable filing | bit | `ProvisioningAFiledRecord_WhenTheMatterBecomesUnreadableBeforeTheColleagues_SkipsThemNamingThatList` | byte-identical |
| Y56 | provisioning's creator decision drops the unreadable-filing fact | bit | `ResumingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefusedNamingThatList` (+1) | byte-identical |
| Y57 | provisioning's forward refusal names the record's own list for an unreadable filing | bit | `ProvisioningAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefusedNamingThatList` | byte-identical |
| Y58 | provisioning's resume refusal names the record's own list for an unreadable filing | bit | `ResumingAFiledRecord_WhenTheMatterItIsFiledUnderCannotBeRead_IsRefusedNamingThatList` | byte-identical |

**58 seeds bit, 0 not bitten** (Y37 / Y46 re-seeded as Y37b / Y46b after their first form did not compile).

Every file restored byte-identical (asserted by the harness) and touched; `git status -- src` clean after the batch. Seeds re-run or added after a later edit: Y17 on its simplified condition (the dead duplicate check removed), Y24 with the parent-list wording now asserted on the creator paths (bit 6), the X16 seed Y52, and Y53–Y58 for the unreadable-filing wording (§16.1, last bullet). **Not seeded, and why:** two conditions were REMOVED rather than left unpinned — the give-back list's duplicate check (an entry can never repeat: one row per record, parent and principal, all from one parent) and the narrower "only when what is left no longer covers" condition on the covering-share marker (an equivalent mutant: the materializer, run at once, records the row covered again — xxix). Two remain as equivalent by construction: `EndWhatAParentPassedOnAsync`'s filter of Revoked rows (the reverse rule answers a Revoked row "done, nothing removed"; only the logged counts differ) and `EndPassedOnAsync`'s skip of a row with no root or principal (such a row names nothing to end).

### 16.4 Placement (CLAUDE.md §10) and component justification (§11)

All in `Sprk.Bff.Api`. **No new service, registration, endpoint, option, job, column, choice option, PCF or package.**
Changed surface, each justified:

| Surface | (1) Existing | (2) Extension | (3) Cost of doing nothing |
|---|---|---|---|
| `SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync` (+ `SecureWallRecordScope`, `SecureShareWallDecision.ParentTable/ParentId/FilingUnreadable`, provisioning's `CreatorWallDecision.FilingUnreadable`, constructor gains `IGenericEntityService`) | `CheckAsync` / `CheckForSecuringAsync` / `CheckProspectiveAsync` (one list each) and three per-parent loops in the creator paths | It IS the extension round 39 names ("ONE guard entry point (SecureShareNoAccessGuard)"); the single-list checks stay as its building blocks; the three loops are folded into it | A person walled off a secure matter could be shared directly on a work assignment filed under it (round 39 item 2), and each new caller would grow a fourth copy of the loop |
| `SecureRootInheritance.ReadSecureParentsAsync` (static) | `FindSecureParentsAsync` (instance) | The same walk, made callable by the guard — which the inheritance depends on, so the guard cannot take the inheritance; the instance method now delegates to it (one copy) | The guard would need a second walk (forbidden: "never a second copy of the parent walk") |
| `EndWhatAParentPassedOnAsync`, `GiveBackAsync` (public), private `EndPassedOnAsync`, `endingParent`; `InheritedUnsharePass.GiveBack` | `PassUnshareOnAsync` (one principal, an isolated parent) | Its loop extracted and reused for all principals of an unsecured parent; the give-back extracted so the unsecure can run it after its flag | Round 39 item 1 unmet: a former sharee keeps a still-secure filed record |
| `ParentMirrorAnswer.PassesNothingOn` / `NotSecure`; `RootFacts.Flag` | `ParentMirrorAnswer.NotIsolated` (one answer for "unsecured", "gone" and "flagged, not isolated") | A flag on the existing answer, the raw flag on the existing facts | The reverse rule cannot tell an unsecured parent (ends) from one mid-provisioning (holds) |
| `PassShareesToFiledRecordAsync` (public) | `SecureIfFiledUnderSecureCoreAsync(allowProvisioning: false)` (private) | A public name for the existing sharee-only pass | Round 47 item 1 (3) has nothing to call |
| `AssignedAccessMaterializer` optional `IServiceScopeFactory? scopes` | `RunAfterWriteAsync`'s scope pattern | The same pattern, injected: the inheritance depends on the materializer (round 47 item 1 (2)), so the materializer reaches the inheritance through a scope (no cycle) | Round 47 item 1 (3) unmet |
| `SecureRootInheritance` constructor gains `AssignedAccessMaterializer` | — | Registered unconditionally in the same module (§10 F.1) | Round 47 item 1 (2): the assignee silently dropped (or Declined) instead of suggested |
| `AssignedAccessReason.CoveringShareEnded` (a `sprk_reason` value) | `RemovedOutOfBand` (→ Declined), `RemovedByNoAccess` (a known cause) | A value of the free-text reason column, on the existing `Skipped` state (re-evaluated every pass) — no schema change | 142's next pass reads the ended coverage as an operator's removal (Declined) and never suggests the assignee |
| `RecheckWrittenSharesAsync` / `ReopenAsync` (private, (e)) | (d) confirms what was written | The same pass, one step later, from fresh reads | §16.1: a share can outlive its source |

**Publish size** (CLAUDE.md §10 item 4, the full convention): `dotnet publish -c Release` (the project's framework-dependent linux-x64), `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**, each side from a FRESH detached short-path worktree (`C:\wt158v2m`, `C:\wt158v2b`, `C:\wt158v2h`), 212 files on every side (no MSB3030 on the base and head publishes; the master side's count matches): `origin/master` `d7c227279` **45.65 MB** (47,868,670 bytes); this round's base `f090b1221` **46.13 MB** (48,366,313); this round's code head `823aebf0f` **46.14 MB** (48,378,811) — the round **+0.01 MB** (+12,498 bytes); the UAC-r2 lane vs master +0.49 MB; far from the ≥ +5 MB escalation and the 60 MB ceiling. The three worktrees were removed afterwards. `dotnet list package --vulnerable
--include-transitive`: none (no package or project reference changed).

### 16.5 Live gates (main session) — what changes

Gate 158-0 (`scripts/Set-AssignedAccessLedgerSchema.ps1` dry run / `-Apply` / `-Verify`, BEFORE the BFF deploy) is
unchanged: this round adds no column or option (`covering-share-ended` is a `sprk_reason` value). It now also guards
the unsecure of a MATTER or PROJECT: without the ledger's `sprk_subjectteam` its Step 4.5 read fails closed (500
`children_incomplete`, the flag kept), and so does a repeat unsecure of an ordinary one. Gate 158-a unchanged.
**Gate 158-b, additionally** (round 39 item 1, on the throwaway secure matter only — never on the shared test project
65a3fab2): share a non-admin test user on the throwaway matter, file a work assignment under it, confirm (read-only) the
user's share on the work assignment and its `inherited:sprk_matter:{id}` ledger row; then unsecure the matter WITHOUT
`alsoUnsecure`; confirm the work assignment is still `sprk_issecure = true`, owned by the Secure Record Owners team, the
user's share on it is GONE (`RetrievePrincipalAccess` without ReadAccess), and the ledger row reads `sprk_state` 100000007
(Revoked) with `sprk_reason` `access-removed`. Then unsecure the work assignment alone (as before). Exact commands: §10
(the 158-a read commands, with the throwaway ids).

### 16.6 Interpretations added this round (owner-reversible; each applies a binding round to a case it does not name)

| # | Decision | Why / alternative |
|---|---|---|
| xxvi | A parent that NO LONGER EXISTS passes nothing on (its rows end by the reverse rule) | Round 39's reasoning: the filed record is still secure and the access came only from the parent's share, which is gone with it. Alternative (held) left a deleted parent's sharees on secure records for good |
| xxvii | Step 4.5 runs BEFORE the flag is cleared (the unsecuring parent excluded from justification), and a failure KEEPS the flag; the give-back runs AFTER it (Step 5.5) | A kept flag keeps the half-done unsecure visible (the job reports the records filed under a flagged-not-isolated parent) and the repeat call takes the full path; a give-back before the flag would be held by that same flagged-not-isolated parent |
| xxviii | Step 4.5 ends every live row the parent's provenance holds — a superset of "each sharee Step 4 revoked" | It also covers the repeat call after a failure (Step 4 then revokes nothing) and a sharee whose parent share could not be revoked (the parent is no longer secure, so that share confers nothing on a still-secure filed record) |
| xxix | Every `CoveredByExisting` row naming the person is marked when the rule removes or narrows the share — not only when what is left no longer covers | The materializer, run at once, records it covered again when it still is: the narrower condition was an equivalent mutant (it changed only writes, never an outcome) |
| xxx | An EMPTY parent flag on the reverse path is unreadable (reported), never "unsecured" | Owner round 17 item 3 |
| xxxi | The Assigned-To suggestion and restore on a filed secure record honour the parents' lists too | Round 39 item 2 says "to every share"; a suggestion the share route would refuse is never offered |
| xxxii | The post-write check (e) | §16.1 |

## 17. Final fix round (`task/uac-r2-158-h`; owner round 56, main-session round 58) — item by item

Branch `task/uac-r2-158-h` from `task/uac-r2-158-r1c-v2` (`e99a329b2`). Binding: owner round 56 (fix (a)–(c), record
(d)–(f) as known limits, no new machinery, this is the lane's last round) and **round 58** (task 158, from the final
re-verification of `task/uac-r2-158-r1c-v2`), on top of every earlier round. No owner question was needed.

| # | Round 58 item | Outcome |
|---|---|---|
| 1 | (a, security) A No Access entry added LATER on a secure matter / project also ends the walled person's DIRECT share on the secure work assignments and projects filed under it — the one parent walk, the existing revoke, never the last reader (S5), failures `children-incomplete` | **Done.** `NoAccessShareEnforcer.EnforceEntryAsync`: after the covered records, for each covered matter / project on which the entry's author holds Write (N5), the secure records FILED UNDER it are listed through the ONE child-direction walk — `SecureRootInheritance.ListFiledRootsAsync`, given a static core exactly as r1c-v2 gave `ReadSecureParentsAsync` one (the instance method delegates; one copy) — and each is enforced by the SAME per-record steps as a covered record (`EnforceOnRecordAsync`: N5 on that record, the per-record lock and its renewal, S5, the revoke and its read-back, its own children via task 149). A filed record not flagged secure yet is reported `not-secure` (Q4 — the inheritance job secures it, the next enforcement reaches it); one whose filing type cannot be read is left alone (nothing removed on a guess); a listing that cannot be read, an undecided record, or any failure on a filed record adds `children-incomplete` naming the parent; the covered-records bound (500) counts the filed records too; a record covered in its own right, or filed under two covered parents, is enforced once; one level (the share-time check reads one level of parents — xxxiii). The **record-scoped** re-apply (task 142's "Update Access" → `EnforceForRecordAsync`) uses the parent walk round 58 names: on a filed work assignment / project it also re-applies the entries covering each secure parent `ReadSecureParentsAsync` finds; an unreadable filing re-applies nothing and is reported. **And the two places that decide WHY a share went** — the inheritance's step (a) and task 142's `Shared` branch — now ask the record's list AND its secure parents' (`CheckRecordAndSecureParentsAsync`, `AsFlagged`; the inheritance over the filing its pass already read): otherwise the enforcer's new removal would read as an operator's (Declined / removed-out-of-band) and never be given again once the wall is lifted (§17.1) |
| 2a | (a) The failed-revoke regression from round 47 item 2: a `Declined` marker written before a revoke that then fails or is not confirmed stays | **Done.** `/unshare-user` keeps `confirmed` and, in the revoke's `finally` (a cancellation too), calls `AssignedAccessMaterializer.RevertShareRemovedAsync` with the rows `MarkShareRemovedAsync` marked — which now returns them as they were BEFORE the marker (every row it marked or tried to: a write that threw may have applied) — writing back each row's prior state and reason. A share the operator did not remove is never on record as declined, and the parent's unshare still ends it. Put back while the share is in fact gone (only the read-back failed), the next pass sees the removal itself (the out-of-band rule) — the safe direction. The verifier's probe (mask 23 kept after the parent's unshare plus a job run) is the regression test, for both a revoke that throws and one Dataverse accepts without applying |
| 2b | (a) Step 4.5's by-parent ledger read counted Revoked / ended rows toward its 5000 bound — a large provenance history could wedge the unsecure | **Done.** `AssignedAccessStore.ReadInheritedLedgerByParentAsync` sends `… and (sprk_state eq null or sprk_state ne 100000007)`: only rows in force count (an EMPTY state reads as Skipped — re-evaluated, never trusted — so it stays in force); rows in force past the bound still report `Truncated` (fail closed). Pinned over the evaluating Web API double (SQL three-valued logic). The fake store's override answers the same contract |
| 2c | (b) The already-ordinary path's 500 said "Calling again completes it" where a repeat cannot (a failed give-back: the rows are ended, so a repeat finds nothing to give back) | **Done.** `UnsecureProjectEndpoint.AlreadyOrdinaryLeftoverDetail`: what could not be REMOVED is still on record — "Run Unsecure on this matter again to remove the rest" (true: a repeat re-reads the rows in force); what could not be GIVEN BACK — "No action is needed for that: it is given back automatically within a few minutes (running Unsecure again does not give it back); open those records' Manage Access to check". Both sentences pinned verbatim, and each test then proves its claim (the repeat removes it; the repeat gives nothing back and the job does) |
| 3 | (f) The Revoked filter in `EndWhatAParentPassedOnAsync` (seed Z28) is equivalent by construction | **Known limit** (§17.7), no test |

### 17.1 Found while fixing (not in the round's list) — fixed here

- **The enforcer's new removal would have been misread as an operator's.** The inheritance's step (a) and task 142's
  `Shared` branch decide why a share went by asking `SecureShareNoAccessGuard.CheckAsync` — the record's OWN list. Once the
  enforcer removes a filed record's share for a PARENT's list (item 1), that check answers "not walled", so the row became
  `Declined` / `removed-out-of-band`: never given again once the wall is lifted (the inherited share — round 30's
  "while the parent share persists"; the Assigned-To share — task 142's criterion 9). Both now ask the guard's one entry
  point (round 39 item 2). Tests: `AShareTheWallRemovedForTheMattersList_IsRecordedAsTheWalls_AndPassedOnAgainOnceItIsLifted`,
  `AnAssignedToShareTheWallRemovedForTheMattersList_IsRestoredOnceItIsLifted`; seeds Z12, Z13.
- **"Update Access" on a filed record** (task 142's record-scoped re-apply) re-applied only the entries naming that record or
  its organizations — so a parent's wall reached it only through the parent's own enforcement or the 5-minute job. It now
  re-applies the secure parents' entries too, through `ReadSecureParentsAsync` (seeds Z23–Z25).

### 17.2 Tests (KEEP paths, ADR-038)

New: `NoAccessShareEnforcerTests` +14 (tests/integration/data-mutation — the enforcer removes access): the walled person's
share on a filed work assignment removed with the matter's; a project filed by the PAIR reached with no share on the matter;
an organization entry reaching what is filed under a covered matter, a record covered twice enforced once; S5 on a filed
record; the listing fault; a filed record's share-read fault; an unreadable filing type; a filed record not yet secure; the
author without Write on the matter; a covered work assignment expands nothing; the covered-records bound; "Update Access"
re-applying the matter's entries, its unreadable filing, its parent truncation. `SecureRootInheritanceRound39Tests` +7
cases (the two classification tests, the probe as a 2-case theory, a confirmed unshare keeps its marker, the two
already-ordinary copies). `AssignedAccessStoreODataTests` +1 (rows in force only; an empty state in force; truncation of
rows in force). One existing assertion refined: `Enforce_WhenNothingWasRemovedOnTheRecord_DoesNotReadItsChildren` now
allows the filed-record listing (`sprk_project` / `sprk_workassignment`) and still forbids any read of the record's children.
Test doubles: the enforcer harness's `Entities()` (the same world the synchronizer reads, as in production); the fake ledger's
by-parent read answers rows in force only. No banned pattern.

| Suite (once, at the end, on the code head `9d2ff7d56`; later commits are docs only) | Result |
|---|---|
| Affected filter (§17.3's 1068 tests) | Passed 1068 / Failed 0 |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | Passed 15422 / Failed 0 / Skipped 54 (Total 15476 = r1c-v2's 15454 + the 22 new cases; 37 m 22 s on a machine shared with other sessions' runs) |
| NetArchTest (`tests/Spaarke.ArchTests`) | Passed 373 / Failed 0 |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (in full) | Passed 104 / Failed 0 / Skipped 0 (Total 104) |
| `tests/integration/Spe.Integration.Tests` (in full) | Passed 403 / Failed 0 / Skipped 25 (Total 428) |

Run sequentially after the seed batch: no contention failure this time.

### 17.3 Seeds (harness `seeds158h.py`: one guard removed per run — a runtime-false condition, or the pre-fix behaviour restored — build, the affected filter (the 158 classes + 142's ledger / materializer / marker / job classes + the share / mirror / guard / provisioning / unsecure / contract / writer / No Access classes); the file restored byte-identical (asserted) and touched; `git status -- src` clean after the batch)

| Seed | Guard removed | Result | Red (first; +n more) | Restored |
|---|---|---|---|---|
| Z01 | round 58 item 1: the filed records are never reached (pre-fix behaviour) | bit | `Enforce_WhenAFiledRecordsSharesCannotBeRead_IsAFailureThere_AndChildrenIncompleteOnTheMatter` (+9) | byte-identical |
| Z02 | a covered WORK ASSIGNMENT is expanded as if it were a parent | bit | `Enforce_AnEntryOnAWorkAssignment_ReachesNothingWhosePairNamesIt` | byte-identical |
| Z03 | N5: what is filed under a record is reached although the author lacks Write on it | bit | `Enforce_WhenTheAuthorLacksWriteOnTheMatter_NothingFiledUnderItIsTouched` (+1) | byte-identical |
| Z04 | a record covered twice (covered, and filed under a covered parent) is enforced twice | bit | `Enforce_AnOrganizationEntry_ReachesWhatIsFiledUnderACoveredMatter_AndARecordCoveredTwiceOnce` | byte-identical |
| Z05 | a record whose filing type could not be read is enforced on a guess | bit | `Enforce_ARecordWhoseFilingTypeCannotBeRead_IsLeftAlone_AndReportedIncomplete` | byte-identical |
| Z06 | a filed record not flagged secure is enforced (Q4) | bit | `Enforce_ARecordFiledUnderTheMatterThatIsNotSecureYet_IsNotTheWalls` | byte-identical |
| Z07 | the covered-records bound is not applied to filed records | bit | `Enforce_MoreRecordsFiledUnderTheMatterThanOneCallCovers_IsTruncated` | byte-identical |
| Z08 | a filed record enforced is not counted | bit | `Enforce_MoreRecordsFiledUnderTheMatterThanOneCallCovers_IsTruncated` (+1) | byte-identical |
| Z09 | the filed records cannot be read and nothing is reported | bit | `Enforce_WhenWhatIsFiledUnderTheMatterCannotBeRead_IsChildrenIncomplete_AndTheMattersRemovalStands` | byte-identical |
| Z10 | a failure on a filed record is not reported as children-incomplete on the parent | bit | `Enforce_WhenAFiledRecordsSharesCannotBeRead_IsAFailureThere_AndChildrenIncompleteOnTheMatter` | byte-identical |
| Z11 | an undecided filed record is not reported | bit | `Enforce_ARecordWhoseFilingTypeCannotBeRead_IsLeftAlone_AndReportedIncomplete` | byte-identical |
| Z12 | round 58 item 1 (inheritance): a removal is classified by the record's own list only (pre-fix) | bit | `AShareTheWallRemovedForTheMattersList_IsRecordedAsTheWalls_AndPassedOnAgainOnceItIsLifted` | byte-identical |
| Z13 | round 58 item 1 (task 142): a removal is classified by the record's own list only (pre-fix) | bit | `AnAssignedToShareTheWallRemovedForTheMattersList_IsRestoredOnceItIsLifted` | byte-identical |
| Z14 | round 58 item 2: the Declined marker is never put back (pre-fix) | bit | `AnOperatorsUnshareThatDoesNotLand_PutsTheMarkerBack_SoTheMattersUnshareStillEndsTheShare(revokeThrows: False)` (+1) | byte-identical |
| Z15 | the marker is put back even after a confirmed removal | bit | `UnsharingAnAutoShare_RecordsDeclined_AndNoLaterSyncSharesAgain` (+3) | byte-identical |
| Z16 | a read-back that still finds the share counts as confirmed | bit | `AnOperatorsUnshareThatDoesNotLand_PutsTheMarkerBack_SoTheMattersUnshareStillEndsTheShare(revokeThrows: False)` | byte-identical |
| Z17 | the marker is put back to another state than the one it held | bit | `AnOperatorsUnshareThatDoesNotLand_PutsTheMarkerBack_SoTheMattersUnshareStillEndsTheShare(revokeThrows: False)` (+1) | byte-identical |
| Z18 | round 58 item 2: ended (Revoked) rows count toward the by-parent bound (pre-fix) | bit | `TheInheritedLedgerOfAParent_CountsOnlyRowsInForceTowardItsBound_AndTruncatesPastIt` | byte-identical |
| Z19 | a row with an EMPTY state is read as not in force | bit | `TheInheritedLedgerOfAParent_CountsOnlyRowsInForceTowardItsBound_AndTruncatesPastIt` | byte-identical |
| Z20 | round 58 item 2: the old copy ('Calling again completes it') (pre-fix) | bit | `UnsecuringAMatterThatIsAlreadyOrdinary_WhenSomethingCannotBeRemoved_SaysToRunItAgain_AndTheRepeatRemovesIt` (+1) | byte-identical |
| Z21 | the copy drops what the operator must do about a failed give-back | bit | `UnsecuringAMatterThatIsAlreadyOrdinary_WhenAGiveBackFails_SaysNoActionIsNeeded_AndTheJobGivesIt` | byte-identical |
| Z22 | the copy never says to run Unsecure again for what could not be removed | bit | `UnsecuringAMatterThatIsAlreadyOrdinary_WhenSomethingCannotBeRemoved_SaysToRunItAgain_AndTheRepeatRemovesIt` | byte-identical |
| Z23 | Update Access on a filed record re-applies its own entries only (pre-fix) | bit | `EnforceForRecord_OnAWorkAssignmentFiledUnderASecureMatter_ReappliesTheMattersEntries` | byte-identical |
| Z24 | an unreadable filing is read as no secure parent | bit | `EnforceForRecord_WhenWhatTheRecordIsFiledUnderCannotBeRead_ReappliesNothing_AndSaysSo` | byte-identical |
| Z25 | a secure parent's truncated entry read is not reported | bit | `EnforceForRecord_WhenMoreEntriesCoverTheMatterThanOneCallReapplies_IsReportedTruncated` | byte-identical |

**25 seeds bit, 0 not bitten.** Z14, Z16 and Z17 first reported "NOT BITTEN" with 2, 1 and 2 failures: the harness's test-name pattern did not match a THEORY's name (`…(revokeThrows: False)`); the pattern was fixed and the three re-run — each bit, as above. Every file restored byte-identical (asserted by the harness) and touched; `git status -- src` clean after the batch. A first start of the batch was stopped during Z02 to add the "Update Access" change (§17.1); its seeded file was restored from the commit and the whole batch re-run on the final code. Not seeded: the `EnforceOnRecordAsync` steps a filed record reuses (N5, lock, S5, read-back, children — pinned by task 143's own tests and seeds) and the static walk's body (moved verbatim; every 158 test of the walk still passes).

### 17.4 Placement (CLAUDE.md §10) and component justification (§11)

All in `Sprk.Bff.Api`. **No new service, registration, endpoint, option, job, column, choice option, PCF or package.**
Changed surface:

| Surface | (1) Existing | (2) Extension | (3) Cost of doing nothing |
|---|---|---|---|
| `NoAccessShareEnforcer` constructor gains `IGenericEntityService` (registered unconditionally, GraphModule) | the enforcer's own store reads entries, people and rights — not filings | the walk needs the app-only Dataverse reads every other filing reader uses; the guard took the same parameter in r1c-v2 | round 58 item 1 unmet: a walled person keeps a direct share on a secure work assignment filed under the walled matter |
| `SecureRootInheritance.ListFiledRootsAsync` static core (+ static `ReadOnePageAsync` / `PairTableOfAsync`; the instance method delegates) | the instance walk | the same walk made callable by the enforcer, which the inheritance's dependency graph reaches (it cannot take the scoped inheritance) — exactly r1c-v2's `ReadSecureParentsAsync` move | a second copy of the child-direction walk (forbidden) |
| private `EnforceOnFiledRecordsAsync` / `FiledIncomplete` / `EntriesCoveringAsync`; `EnforceOnRecordAsync` answers whether N5 held | `EnforceOnRecordAsync`, `SyncChildrenAsync`'s `children-incomplete` | the existing per-record steps, called for each filed record; the record-scoped covering read extracted to serve the record and each parent | item 1 unmet |
| `AssignedAccessMaterializer.MarkShareRemovedAsync` returns the rows it marked; `RevertShareRemovedAsync`; private `MarkRowsAsync` | `MarkAsync` (count only) | the same marker, keeping what it overwrote; `MarkAsync` keeps its count contract | item 2a unmet: a share not removed reads declined and outlives its parent's unshare |
| `UnsecureProjectEndpoint.AlreadyOrdinaryLeftoverDetail` | one fixed sentence | the same 500, its copy built from the two counts it already had | item 2c unmet: the copy promises what a repeat cannot do |
| `ReadInheritedLedgerByParentAsync` filter | the same read | one clause | item 2b unmet: a long provenance history wedges the unsecure |

**Publish size**: (CLAUDE.md §10 item 4, the full convention): `dotnet publish -c Release` (the project's framework-dependent linux-x64), `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**, each side from a FRESH detached short-path worktree (`C:\wt158hb` at this round's base `e99a329b2`, `C:\wt158hh` at the code head `9d2ff7d56`), 212 files on each side, no MSB3030: base **46.14 MB** (48,378,863 bytes); head **46.14 MB** (48,383,026 bytes) — the round **+0.00 MB** (+4,163 bytes); far from the ≥ +5 MB escalation and the 60 MB ceiling. Both worktrees were removed afterwards. Master was not re-published this round: the round's own delta is the measurement that isolates it (r1c-v2 measured `origin/master` `d7c227279` at 45.65 MB, the lane +0.49 MB). `dotnet list package --vulnerable --include-transitive`: none (no package or project reference changed).

### 17.5 Live gates (main session) — what changes

Gate 158-0 unchanged (no column or option this round). Gate 158-a unchanged. **Gate 158-b, additionally** (round 58 item
1, on the throwaway secure matter only): with a non-admin test user shared DIRECTLY on the secure work assignment filed
under the throwaway matter (`/share-user` on the work assignment), add a No Access entry naming that user on the MATTER
(its No Access list in the model-driven app; the save calls `POST /api/v1/external-access/no-access/enforce`) and confirm,
read-only, that the user's share on the WORK ASSIGNMENT is gone (`RetrievePrincipalAccess` without ReadAccess, the §10
command with the throwaway ids) while the work assignment stays secure and team-owned; deactivate the entry afterwards.

### 17.6 Interpretations added this round (owner-reversible; each applies round 58 to a case it does not name)

| # | Decision | Why / alternative |
|---|---|---|
| xxxiii | The enforcer reaches the records filed DIRECTLY under a walled matter / project (one level), not records filed under those | The share-time check (round 39 item 2) reads one level of parents; a record whose shares the guard allows must not be stripped every five minutes by the job (flapping). A deeper record honours its own parent's list |
| xxxiv | N5 holds per record: the author must hold Write on the walled matter / project to reach anything filed under it, AND on each filed record to remove there | Owner N5 ("Write on the record"); the alternative (the parent's Write alone) would let an entry strip access on a record its author cannot change |
| xxxv | A filed record not yet flagged secure is reported `not-secure`, never enforced | Q4 (the internal wall is for secure records); the inheritance job secures it (≤ 5 min) and the next enforcement removes the share (≤ 5 min) |
| xxxvi | The enforcer removes the walled person's WHOLE direct share on a filed record (round 58's "removes or narrows") | A walled person keeps nothing; a narrowing would leave Read. The "narrowing" that does happen is round 30's: a raised share put back when a parent's own share ends |
| xxxvii | A Declined marker is put back whenever the revoke is not CONFIRMED — including when only the read-back failed | Keeping it while the share might remain is the defect round 58 names; putting it back while the share is gone is recovered by the next pass (out-of-band rule) |

### 17.7 Known limits (owner round 56 classes (d)–(f); one line each — also for the PR description)

- (f) The Revoked filter in `EndWhatAParentPassedOnAsync` (seed Z28) is equivalent by construction — the reverse rule answers a Revoked row "done, nothing removed", and since this round the by-parent read leaves Revoked rows out in the query — so no test is added (round 58 item 3).
- (e) A revoke that is not confirmed AND a ledger that cannot take the put-back (a double fault) leaves the Declined marker; the 500 asks the operator to try again, and a retry that lands makes the marker true (logged).
- (e) The put-back writes each row's pre-marker state; a pass that changed the same row in the milliseconds between the marker and the failed revoke is overwritten, and the next pass re-decides that row from the record's actual shares (If-Match on ledger writes lands at integration — round 47 item 2).
- (e) The enforcer's covered-records bound (500 per entry per call) now counts filed records too; an entry covering more is reported `Truncated` on every run in the same order — the existing property of that bound, extended.
