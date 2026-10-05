# Task 158 — a work assignment or project filed under a SECURE matter or project is itself secure (owner round 6)

> Branch `task/uac-r2-158` = `task/uac-r2-148-r2` + `task/uac-r2-156-c1-r2` (merged, conflicts resolved preserving both
> intents) + `work/unified-access-control-r2`, base **`c8a964cb69a82f9a2e697d12535f097b9227055f`**.
> **Fix round r1** (§14): `task/uac-r2-158-r1` = 158 + `task/uac-r2-142-r6` (merge `5d041a627`, the `sprk_assignedaccess`
> ledger round 30 extends) + `a58cbd8ee`; stopped by a machine restart (WIP `c5decdbeb`); completed on
> **`task/uac-r2-158-r1c`** (base `c5decdbeb`).
> Status: **code complete, not deployed.** Live steps are manual gates for the main session (§10).
> Binding owner decisions: round 6 items 1, 2, 4 and the item-4 clarification; round 3b / round 10 item 7 (F3, via task
> 146-c1's `SecureDesignationRemoval`); round 7 item 3 (G5 chat create); round 8 item 1 + round 13 item 7 (§6.5 path B
> for app-only follow-ons of the user-OBO tools); round 17 item 3 (an EMPTY `sprk_issecure` is never "not secure");
> S5 (round 3: a secure record always has at least one person who can see it); R3/R4 (minutes, never hourly);
> **round 30** (inherited-share provenance on task 142's ledger, option (c)); **round 31** (the creator honours the
> record's AND every secure parent's No Access list before any write; a record created under a secure parent is created
> INTO isolation); **owner round 32** (§6.5 path B, secure inline — ACCEPTED).

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
| Never auto-unsecure | nothing in this task takes a record out of isolation; re-filing it elsewhere or unsecuring its parent leaves it secure (round 6 item 4) |
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
record outside the BFF → `Declined` (`/unshare-user` on the filed record marks it `Declined` through 142's marker);
(b) a row whose parent is isolated and no longer shares the principal is ended by the reverse rule — a `Declined` row ends
too ("never re-added WHILE the parent share persists"); a parent that cannot be read is reported. (c) the add-only mirror,
never re-adding a declined principal. (d) the provenance of what it wrote. An unreadable provenance gives nobody anything;
an unwritable one is reported.

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
| xiii | An **unsecured** parent ends nothing it passed on: its filed records keep the inherited shares (and `/unshare-user` on the now-ordinary parent ends none of them); the job's reverse rule acts only on an isolated parent | Round 6 item 4: the filed records "STAY secure", as they were. Round 30 is about a sharee removed from a SECURE parent; an unsecure widens the parent (its people keep reaching it by business unit). Alternative (remove every inherited share on unsecure) rejected: it would change records round 6 says stay as they are |
| xiv | A record **re-filed away** from a secure parent keeps what it passed on until that parent's unshare (BFF or out of band) | Re-filing is not an unshare; never lower existing access (A4). The job reaches it through the parent's own ledger rows |
| xv | The reverse rule decides "still justified by another parent" on the **intersection** with task 149's rule for untrusted parents: a read parent that no longer carries the person ends it; every read parent carrying it while another cannot be trusted holds it (reported) | Owner round 11 item 4 (intersection) + 149's "a principal its known roots do not share is revoked; held children are only narrowed". The first round kept everything when any parent could not be trusted (fail-open on removal) |
| xvi | The unsecure of a filed record ends its inherited rows FIRST (Step 3.6) and stops (children_incomplete, retryable) if it cannot | A row left `Shared` after Step 4's revoke reads, at a later re-secure, as an operator's removal |
| xvii | Inherited rows are left out of task 142's ledger-roots scan (`IsAssignedToScanRow`) | They are not Assigned-To rows (no contact / organization subject — the materializer already ignores them); counting them would truncate and fail 142's job in a large environment |
| xviii | A stranded isolated create (creator not shared AND the row not deletable) is named to the user (chat error / Office warning) | Never "shared to you" for a row only an administrator can open; the job shares it to `sprk_createdbyperson` |

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
`StillJustifiedByParentsAsync` over `IsolatedParentMirrorAsync` (interpretation xv).

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
