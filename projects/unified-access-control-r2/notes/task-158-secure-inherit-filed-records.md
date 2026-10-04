# Task 158 — a work assignment or project filed under a SECURE matter or project is itself secure (owner round 6)

> Branch `task/uac-r2-158` = `task/uac-r2-148-r2` + `task/uac-r2-156-c1-r2` (merged, conflicts resolved preserving both
> intents) + `work/unified-access-control-r2`, base **`c8a964cb69a82f9a2e697d12535f097b9227055f`**.
> Status: **code complete, not deployed.** Live steps are manual gates for the main session (§10).
> Binding owner decisions: round 6 items 1, 2, 4 and the item-4 clarification; round 3b / round 10 item 7 (F3, via task
> 146-c1's `SecureDesignationRemoval`); round 7 item 3 (G5 chat create); round 8 item 1 + round 13 item 7 (§6.5 path B
> for app-only follow-ons of the user-OBO tools); round 17 item 3 (an EMPTY `sprk_issecure` is never "not secure");
> S5 (round 3: a secure record always has at least one person who can see it); R3/R4 (minutes, never hourly).

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
| Secure-if-any | one readably secure parent is enough. An unreadable / EMPTY parent flag or an unresolvable pair is never "not secure": with no other secure parent → **unverifiable** (nothing written; a BFF write refused); beside a readably secure parent → the record IS secured but its sharee mirror is **held** (interpretation iii) |
| Never auto-unsecure | nothing in this task takes a record out of isolation; re-filing it elsewhere or unsecuring its parent leaves it secure (round 6 item 4) |
| Sharees | task 149's mirror (rights ∩ Read/Write/Append/AppendTo/Delete, never Share/Assign; INTERSECTION over several secure parents; the No Access guard; a flagged-not-isolated parent holds), **add-only on a root** (interpretation iv) |

## 2. Inventory (step 1)

### 2a. BFF paths that create or re-file a work assignment or project with a filing link

| # | Path | Create / re-file | Wired |
|---|---|---|---|
| W1 | `dataverse.create_record` (`DataverseCreateRecordHandler` → `OwnedChildWrite`) | create (any table) | pre-check + secure after create; an incomplete securing answers an ERROR naming the record |
| W2 | `dataverse.update_record` (`DataverseUpdateRecordHandler`) | re-file | pre-check + secure after the caller's PATCH; incomplete → error |
| W3 | Playbook output orchestrator `DataverseUpdateHandler` | re-file | pre-check (throws `RecordOwnerUnresolvedException`) + secure after; incomplete logged → job |
| W4 | UpdateRecord node / ActionSeam `UpdateRecordActionCore` | re-file | pre-check (scope-resolved gate; no gate in the host → refuse a filing write) + secure after |
| W5 | Field-mapping push `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` | re-file (bulk) | pre-check per record (refusal = that record failed) + secure after; no gate → refuse a filing write |
| W6 | Office quick-create `RecordCreationService.CreateProjectAsync` (pair via the Field Mapping Framework) | create | pre-check (`OwnerUnresolved` failure) + secure after; incomplete → warning |
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
- **(1) create, (2) re-file** — `SecureRootFilingGate` (singleton; scope factory; registered beside the restamper by
  `AddCoreAncestorResolver`): `CheckAsync` BEFORE the write (reads the row as the write will leave it — the writers'
  spellings: logical name or `Nav@odata.bind`; `EntityReference`, `Guid`, bind path, GUID text, JSON string, null —
  and refuses `record_owner_parent_undetermined` when no parent is readably secure and one cannot be read, or a value
  cannot be interpreted), `SecureAfterWriteAsync` AFTER it with the write's own columns (a write — create or update —
  that files nothing costs no read; never throws; a failure is logged and left to the job).
- **(3) a parent becomes secure** — `ProvisionProjectEndpoint` Step 8 (task 148's `ChildrenFollowAsync`): after the
  record's children, `SecureFiledRootsUnderAsync` secures every work assignment / project filed under it (each through
  `ProvisionInheritedAsync`, whose own Step 8 secures what is filed under IT — the tree, bounded by
  `MaxNestedProvisioning = 3`, deeper records left to the job, `sdap.inherit.too_deep`). Incomplete → the existing
  500 **`sdap.provision.children_incomplete`** with `filedRecordsSecured` / `filedRecordsRemaining` / `filedRecords`
  (the client already classifies the code — interpretation vi); the 200 carries `filedRecords`. The already-provisioned
  branch runs the pass too (200 `childrenOnly` when it secured something; 409 `already_provisioned` otherwise).
- **(4) writes outside the BFF** — `SecureRootInheritanceJob` (ADR-036 `IScheduledJob`, `*/5 * * * *`, writes on,
  `AddScheduledJob` in `ExternalAccessModule`): lists every flagged matter/project (paged; past 20 pages → throw), the
  records filed under them (one page per query; more → throw), secures each (not-yet-secure first, at most
  `MaxProvisioningsPerRun = 25` provisionings per run, the rest deferred and counted), gives every secure one its
  parents' sharees; a failed scan is a failed run (throws); any incomplete record → `Success = false` naming it;
  heartbeat per attempt. **POML escalation trigger does not fire**: the job reads DATA, not the path that wrote it, so
  no writer outside the BFF can hide a filed record from it for more than one run.

## 4. Sharees — `SecureChildShareSynchronizer.SyncInheritedRootAsync`

Task 149's mechanism applied to a ROOT filed under secure parents: the parents' direct shares (strict read), the
intersection over isolated parents, `ChildMirrorMask`, the No Access guard on the record and every parent (walled →
dropped; unverifiable → held), a flagged-but-not-isolated parent → held (`Incomplete`), then GRANT a missing principal or
MODIFY to `current | mirror`; read back through the strict read; then 149's `RunAsync(record)` for the record's own
children when anything was written. **It never revokes or narrows** (interpretation iv).

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
| i | A work assignment / project created under a secure parent is created as an **ordinary row of the caller's business unit** (chat: `OwnedChildWrite` re-resolves the owner with no parents when the resolver answers the Secure team for a root; Office: the caller's team as before) and **then provisioned forward** (flag → creator share → move → container) in the same call | S5 (round 3, binding): "never create a record nobody can see". Creating it owned by the memberless team and resuming would leave a window — and, on any failure, a record — that nobody can open. Cost: between the create and the move (normally the same request) it is readable by its business unit, exactly as a record a user then marks secure; if securing fails, the writer says so (chat error / Office warning) and the job retries every 5 minutes |
| ii | Parents are matters and projects only | the round-6 question as asked; a pair naming an intermediate (event, invoice) is task 155/156's domain |
| iii | Secure-if-any with an unreadable sibling: secure, hold the sharees, report incomplete | securing is the closed direction; mirroring without the intersection could give a principal of one parent access the other forbids |
| iv | **Add-only** mirror onto a root: a parent's later sharee is added; a parent's UNSHARE is not propagated, and the root's own shares (its creator, its Manage Access grants) are never narrowed or revoked | A root's shares cannot be told apart as "mirrored" or "granted on the record itself" (a View Only grant and a View Only mirror are the same mask; no provenance column exists). **Open question for the owner (§11 Q1).** |
| v | `POST /api/v1/work-assignments` not modified | task 166 deletes it (§2a) |
| vi | Incomplete filed records reuse `sdap.provision.children_incomplete` | the client already classifies it; a new code would be an unrecognised 500 in the wizard |
| vii | F3 is task 146-c1's helper, byte-identical (blob `13ba8f80b`, identical on `integ/uac-r2-batch4`) | "exactly one F3 check"; task 150's unsecure F3 gate is not on this base — the main session keeps one (§13) |
| viii | The user-OBO chat tools secure INLINE (app-only provisioning after the caller's own write) | **§6.5 path B extension PROPOSED** beside round 8 item 1 / round 13 item 7 (§11 Q2) |
| ix | Nesting bound 3 per call | a pathological project chain cannot make one request unbounded; the job completes deeper levels |
| x | Listing is type-exact; a pair whose type cannot be read is an **unconfirmed candidate**: the transition and the job decide it (→ unverifiable, reported), the unsecure endpoint never shows it as related | the job must not miss a record; the unsecure list must never offer an unrelated record |
| xi | A record filed under a parent flagged secure but not isolated is secured (the flag is what it inherits); no sharee is mirrored from that parent until it is isolated | 149's rule for children, applied to a root |
| xii | A parent's later sharee reaches its SECURE filed records **inline**: `/share-user` calls `SecureRootInheritance.PassSharesOnAsync` after task 149's child fan-out, and a project that was just GIVEN sharees passes them on to what is filed under it in the same call (the nested-provisioning order: a work assignment secured inside a project's provisioning, before that project had its matter's sharees). A sharee-only pass never provisions (`sdap.inherit.not_yet_secure`: the job secures it); bounded by the same depth; never fails the share (logged; the job completes). A model-driven-app Share of the parent reaches them through the job | owner R3/R4 ("immediate on save; the job only a safety net"); task 149 fans out to children inline the same way. `InternalShareEndpoints.ShareAsync` gains one dependency — a hand-merge point with 142 (§13) |

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
  create of the caller's unit (was: refused), and an unreadable parent refuses.
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

Writes, so pending; run after the BFF carrying this task is deployed to dev.

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
2. **Gate 158-b (unsecure listing).** After 158-a, `POST /api/v1/external-access/unsecure-project` is NOT run on the
   shared test project (it would unsecure it); verify the listing on a throwaway secure matter instead (create, provision,
   file a work assignment, unsecure the matter with no `alsoUnsecure`, confirm the response lists the work assignment and
   it stays secure; then unsecure it alone).
3. **No first-run effect:** §2b — 0 candidates in dev today; re-run the §2b counts immediately before deploy.

## 11. Questions for the owner / main session (not escalation triggers; the work is complete under the interpretations)

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
  - *Implemented as proposed* (the class remarks of `DataverseUpdateRecordHandler` mark it PROPOSED); the main session
    records the amendment or tells this task to fall back to C.

## 12. Handovers

- **Task 150 (ribbon / Access group, round 26):** render `relatedSecureRecords` with a checkbox each; post the ticked
  ones in `alsoUnsecure`; show `relatedRecordsUnsecured` outcomes; map `sdap.unsecure.parent_still_secure` (409, names the
  parent) and `sdap.unsecure.parent_unverifiable` (500) to copy. The provisioning wizard already handles
  `children_incomplete`; `filedRecords` is additive.
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
- `.claude/` edits needed: **none**.
