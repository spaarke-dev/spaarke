# Task 022: Create Project wizard (including secure Project) on the server write path (FR-16) — code part

> Rigor FULL. Branch `task/fm-022`, from 9e68547626 (the work branch tip). **Code part only.** Not done here (owner decision: the NFR-02 verdict is taken in UAT): the deploy, the live check and the NFR-02 run. Nothing in this branch has been exercised live. Reference implementation: task 021 (`notes/task-021-notes.md`).

## What changed

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts` | `createProject` now always creates through `createRecordViaBffWithWarnings(authenticatedFetch, bffBaseUrl, 'sprk_project', entity, options)`. Never `dataService.createRecord` / `Xrm.WebApi`. **Fails closed**: with no `authenticatedFetch` or no base URL it writes nothing and returns "The project was not created: this screen is not connected to the Spaarke service, which must save it." Deleted: the `applyFieldMappings` call, the BU cascade (`applyUserBuDefaults` → `sprk_searchindexname`), the `sprk_ai_search_index` bind, the `routesChildWritesThroughBff` branch and the client `syncAssignedAccess` call. `options` = `mappingSource` from the AssociateToStep selection (query string only) + `secure: true` when `isSecure`. `securedAtCreate` is set on every successful secure create. Errors go through `describeWebApiCreateError(err, 'project', { rootCreate: true })`. `cascadeDefaults` is kept as `_cascadeDefaults` (accepted, not read) so the hosts compile unchanged. |
| `.../CreateProjectWizard/CreateProjectWizard.tsx` | Removed the step-1e client Assigned-To sync after secure provisioning (and its import). The BU resolve was kept as in 021 in the first round (removed in fix round 1, below). `indexUploadedFiles` no longer passes `cascadeDefaults?.searchIndexName` (FR-40: the server's `FileIndexRequest` has no index name). Comments on steps 1a, 2 and the no-BFF secure branch rewritten. Filing (N:N `$ref` + `fileProjectUnderMatter`), the account association and the task-150 hold-back are unchanged. |
| `.../__tests__/projectService.fieldMapping.test.ts` | Rewritten, 19 tests (below). |
| `.../__tests__/projectService.test.ts` | Converted (deviation 2). |
| `.../__tests__/secureFromCreation.test.ts` | The undecorated-data-service case now expects `?secure=true`, `securedAtCreate: true` and no Xrm create. |
| `.../__tests__/CreateProjectWizard.secureHoldBack.test.tsx` | Expected sequences `['create-project','provision','assigned-access-sync']` → `['create-project','provision']`; new `it.each([true,false])`: no client sync, `indexUploadedFiles` called with 4 arguments. |
| `.../__tests__/CreateProjectWizard.provisioningHost.test.tsx` | "syncs after provisioning" → "makes no client Assigned-To sync for a SECURE project". |
| `src/.../services/__tests__/assignedAccessSync.test.ts`, `src/.../utils/__tests__/webApiCreateError.test.ts` | Outside the wizard folder, but both drive `ProjectService.createProject` (deviation 1). |

Hosts: **no change in the first round** (fix round 1 removed only their `resolveUserBuDefaults` wiring). `src/solutions/CreateProjectWizard/src/main.tsx` already passes `authenticatedFetch` and `resolvedBffBaseUrl`; `InAppWizardHost` and `SummarizeFilesDialog` pass the same two. Each host picks up the new path only when its own bundle is rebuilt (the shared library is compiled into each). `provisioningService.ts`: no change (049 already treats `already_provisioned` as success when `securedAtCreate`). `SummarizeFilesDialog.tsx:559` already passes `securedAtCreate`, so its secure path keeps working now that every secure create carries the intent. BFF: not touched.

## Client-default inventory (POML step 2)

| Key | Class | Now |
|---|---|---|
| `sprk_projectname`, `sprk_projectdescription` | user value | sent |
| project type, practice area, attorney (hint `attorney`), paralegal (hint `paralegal`), outside counsel binds | user pick | sent (nav-prop discovery unchanged) |
| `sprk_searchindexname` (BU cascade) | client default | **dropped**: server-owned on a project (`SearchIndexed = true`, FR-38; the index follows the content type, FR-40) |
| `sprk_AI_Search_Index@odata.bind` (BU cascade) | client default | **dropped**: same rows |
| mapped fields (`applyFieldMappings`) | client mapping | **dropped**: the server maps fill-only from `mappingSource` (FR-02) |
| `sprk_issecure` | never sent (task 150) | still never sent; intent goes as `?secure=true` |
| `sprk_containerid` | never sent (task 076) | still never sent (provisioning's) |
| owner / owning team | never sent | still never sent (FR-09: the server decides — see "Owner" below) |

## Contract check (BFF code at the work-branch tip)

- `ChildRecordEndpoints.CreateAsync` (~:267-333): `POST /api/v1/child-records/{table}` with `mappingSourceEntity`/`mappingSourceId` (`MappingSourceOf`) and `secure` (`SecureIntentOf`; `SecureIntentTables` = `sprk_project`, `sprk_workassignment`; any other table → 400 `child_record.secure_intent_invalid`). 201 with `{id}` or `{id, warnings}`. Refusals: `RefusalResult` maps the delegation codes through `DelegationRuleFilter.DenyResult` (403 `sdap.access.deny.delegation_write_required` / `_share_required` / `_check_failed`); `child_record.not_found` → 404 uniform.
- `RecordTableRules.cs` `sprk_project` (:190-218): `MappingProtectedColumns`, `AutonumberColumn = sprk_projectnumber`, `ForPerson = sprk_assignedtointernal`, `TableServerOwnedColumns = RootServerOwnedColumns` (container, security BU, access permission), `SearchIndexed = true`, `OptionalReferenceLookups = sprk_projecttype_ref` (Office path only; the route does not pass it, so an unusable type refuses the create as the old Xrm path did, same reasoning as 021's "Decision").
- `InferMappingSourceTables` includes `sprk_project`, but the payload carries no filing key, so the source must be named explicitly — the wizard does.
- 049/049b/049c: secure intent creates into isolation; root create grants the maker's share inline and queues the rest; for a secure-from-creation project the grant waits for the securing, the reconciliation job finishes it. Hence no client sync.
- `ProjectMatterFiling` (PATCH, `fileProjectUnderMatter`): writes no owner; under a secure matter → 409 (`SecureMatterCode`, #1506).
- **Owner (corrected in fix round 1, K2).** The create names no parent, so `DecideOwnerAsync` (`RecordCreationService.cs` ~:1130) falls through to the **acting user's business-unit team**: a wizard project is NOT owned record-first at create. The matter's team takes over only after `fileProjectUnderMatter` succeeds and the existing ownership jobs run (root-owner step, ordinary-moves pass): `ProjectMatterFiling.cs:26-27`, "nothing here writes an owner". The first version of these notes and the `projectService.ts` comments said "record-first"; both are corrected.

## Decision: `rootCreate: true` (deviation from the brief)

The brief said "a child create, so use the child-records route with `mappingSource*`, not `rootCreate`". The route and `mappingSource*` are used; **`rootCreate: true` is still correct** for the error text: the create names no parent. Task 049's known limit ("an intent create filed under a secure record is refused"; "the Create Project wizard files a project only after it is created") keeps filing post-create (N:N `$ref` + PATCH), so the create body has no regarding pair, and a 404 `not_found` can only mean one of the user's picked values. The "filed under" sentence would be wrong. Tested ("the Project wizard shows the root-create sentence too, never 'filed under'").

**Open owner option (not done):** send the matter pair in the create for non-secure projects. That would give the matter's team as owner at create (instead of the caller's business-unit team until the jobs run) and refuse a project under a secure matter at create instead of a 409 on the later PATCH. It needs an owner decision and server-side N:N link writing (#2524, never implemented).

### Deviations

1. Two test files outside the wizard folder changed (`assignedAccessSync.test.ts`, `webApiCreateError.test.ts`), as in 021 deviation 2: both drive `ProjectService.createProject`. The Xrm.WebApi wizard section of `webApiCreateError.test.ts` was removed (no Xrm create wizard is left); its two form fixtures were kept.
2. `projectService.test.ts` converted: the cascade must never reach the body (the nav-prop metadata still offers `sprk_AI_Search_Index` to prove the bind is not written); `applyUserBuDefaults` not called; the static INV-5 helper test kept; the error path runs through the route.
3. The wizard's secure post-provision client sync removed (the server grants; see contract check).
4. The BU-resolve failure warning removed from the UI (its value is no longer read), as in 021.

## Tests (failing first)

`projectService.fieldMapping.test.ts` (19): body = user keys only; no owner/container/index/issecure key for both `isSecure` values; the route is called, never `dataService.createRecord`; undecorated and decorated data services make the same call with `?secure=true`; `mappingSource` in the query and the body identical with/without it; combined query `?mappingSourceEntity=sprk_matter&mappingSourceId=<id>&secure=true`; `applyFieldMappings` never called; no sync call; source scan (no `applyFieldMappings`, `FieldMappingService`, `applyUserBuDefaults`, `syncAssignedAccess`, `_dataService.createRecord`); server warnings reach the result; 400 → "Failed to create project record: Name is required."; 404 → root sentence; delegation `share_required` / `check_failed` sentences; fail-closed ×3 (no fetch, no base, empty base).

Fail-then-pass (the 22 suites touching ProjectService / the wizard / the two shared test files):
- RED, with HEAD `projectService.ts` and `CreateProjectWizard.tsx` copied back and the new tests: **41 failed / 339 passed** (7 of 22 suites failed).
- GREEN: **380 / 380**, 22 suites.

Full `Spaarke.UI.Components` jest: 6048 passed / 6 failed in 4 unrelated suites (`OntologyAdmin/Rules`, `EmailComposer/templatePicker`, `AccessGrantModal.inviteRetryEmailSent`, `DecisionWizard`). Re-run alone: **4/4 suites, 107/107 pass** — timing flakes under the full parallel run (81-134 s suite times).

## Verification

- `tsc --noEmit` (Spaarke.UI.Components): exit 0.
- client-lint ratchet (`scripts/quality/client-lint`, after `npm ci` there): ok, no suppression change.
- Vite build `src/solutions/CreateProjectWizard` (Spaarke.Auth and Spaarke.SdapClient built first): `dist/index.html` 1,176.24 kB (318.99 kB gzip), exit 0. **Not deployed.**
- BFF: not touched, so no BFF build/test.

## §11

First round: no new surface. `IBffCreateOptions` and `createRecordViaBffWithWarnings` already existed (021/049).

Fix round 1 adds one exported function, `saysProjectNotCreated(message)` in `projectService.ts`:
1. **Existing**: `describeWebApiCreateError` maps errors to sentences but does not say whether a sentence already reports the failure; no helper does (grep `was not created` in `src/client`).
2. **Extension**: it is a one-line predicate beside `createProject` (same file). `SummarizeFilesDialog` needs the same test for its own prefix, so it is exported rather than duplicated as a regex in two files.
3. **Cost of doing nothing**: the user reads "Project creation failed: Failed to create project record: The project was not created: …" (K1).

Removed surface: the `resolveUserBuDefaults` prop of `CreateProjectWizard` (and its two host wirings).

## Steps left before deploy (deploy order)

1. **HARD GATE — both BFF changes live first: the `sprk_project` child-records route (task 020) AND the `secure` query parameter (task 049 fix round 2).** Check both on the target environment before any host below is deployed. Without the route, every Project create from a rebuilt host fails (fail-closed, nothing written). Without `secure`, a BFF that ignores the parameter **silently creates an ordinary project** for a secure request: the shield then rests on provisioning alone, which is the transient-ordinary window task 049 closed.
2. `sprk_createprojectwizard` via `code-page-deploy` (keep the rollback bundle).
3. Rebuild and deploy the other hosts that bundle the shared `ProjectService`: `SummarizeFilesWizard` (`sprk_summarizefileswizard`), SpaarkeAi / LegalWorkspace (`InAppWizardHost`, `sprk_spaarkeai`), and the `Spaarke.AI.Widgets` consumers. Until each is rebuilt it stays on its old bundled path.
4. Live checks: standard project (with and without a matter; mapping fill-only; number issued; owner = the caller's business-unit team after the create, the matter's team once filed and the jobs have run; Assigned-To access granted by the server), secure project (created secure, provisioning `already_provisioned` → success, hold-back).
5. NFR-02 verdict in UAT (owner decision).

## Found in passing (not fixed; out of scope — items fixed in fix round 1 are listed there)

- The server does not write the `sprk_Project_Matter_nn` link on create (#2524); the client `$ref` (`CreateProjectWizard.tsx:67`) is still its only writer.
- A non-secure project filed under a secure matter after create gets 409 from `ProjectMatterFiling` (#1506) — pre-existing; see the open owner option above.
- Four UI.Components suites time out under the full parallel jest run (listed above), pass alone.
- The Matter wizard still makes the same wasted `resolveUserBuDefaults` call (`CreateMatterWizard.tsx` ~:558), and `InAppWizardHost` and the CreateMatterWizard code page still wire it. That is 021's file and was left alone here.
- client-lint `check` in this worktree fails for 8 packages that have no `node_modules` installed here (React types do not resolve, so `no-unnecessary-condition` fires on unrelated files, e.g. `Reporting/ReportDropdown.tsx:278`). The merge did not touch those files. `ui-components`, the only package changed here, is `ok`.

## Fix round 1 (Opus adversarial review, 2026-11-08)

The review found no fix-now defect in 022 itself. The work branch was merged in first (tip b05480a11e; merge, no rebase).

- **K2, owner comments.** `projectService.ts` (method doc, payload comment, create comment) and this file's contract check now say the create's owner is the acting user's business-unit team, and the matter's team only after the filing and the jobs. Before, they said "record-first".
- **K1, double/triple prefix.** `createProject` no longer puts "Failed to create project record:" in front of a detail that already says the project was not created or saved (`saysProjectNotCreated`). `SummarizeFilesDialog` applies the same rule to its own "Project creation failed:" prefix. Tests: an `it.each` with 409/400/503 details, a predicate test, and the dialog's no-BFF test.
- **Found-in-passing items, now fixed (no longer listed above):**
  - `resolveUserBuDefaults` is removed from `CreateProjectWizard`: the prop, the call, the effect dependency and the import. Its wiring is removed from `InAppWizardHost` (Project case) and `src/solutions/CreateProjectWizard/src/main.tsx` (the callback and the now-unused `EntityCreationService`/`IUserBuCascadeDefaults` import). `createProject` keeps its `_cascadeDefaults` parameter, which the wizard passes as `undefined`.
  - Unreachable branches removed: the wizard's "secure requested, no BFF" warning and `SummarizeFilesDialog`'s "created but not secured" branch. In both, the BFF check is folded into the secure condition (type narrowing only). The success title stays keyed on `provisioningSucceeded`, so the hold-back is unchanged. The owner-picked F6 row 4 copy is gone with its unreachable state. Its test now runs the REAL `ProjectService` with no BFF and expects the single, unprefixed refusal.
  - The unused `_orgUrl` in `associateProjectWithAccount` is removed.
- **Test gap (secure + mapping source).** The single-matter test is now an `it.each` over a matter source and an account source. Each asserts the query `?mappingSourceEntity=…&mappingSourceId=…&secure=true`, no source id and no `issecure` in the body, and `securedAtCreate`.
- **Notes.** The deploy order's step 1 is now an explicit HARD GATE: both the 020 route and the 049 `secure` parameter.

Fail-then-pass (fix round 1): with the old prefix rule in `projectService.ts` and the HEAD `SummarizeFilesDialog.tsx`, **4 failed / 24 passed** (2 suites). With the fix, the touched folders (`CreateProjectWizard`, `SummarizeFilesWizard`, `Wizard`, `assignedAccessSync.test.ts`, `webApiCreateError.test.ts`) pass **425 / 425 in 25 suites**. `tsc --noEmit`: exit 0. client-lint: `ui-components` ok (see above). Vite build of `CreateProjectWizard`: `dist/index.html` 1,175.56 kB (318.88 kB gzip), exit 0, not deployed.

rework: fix-round-1 task 022 (K1 prefix, K2 owner wording, three found-in-passing items fixed, secure+source test, deploy hard gate)

## Fix round 2 (the mapping-source rule, 2026-11-09)

**The rule.** Values mapped from a secure source are written only into a row owned by that source's isolation owner. "Secure" is the 032b copy gate's secure-if-any predicate (`MappingSourceCopyGate.DecideAsync`): a secure-flagged root whose flag is not an explicit `false`, or a source owned by the Secure Record owner team. No second predicate was added.

**Step 3 narrows early** (`RecordCreationService.Mapping.cs`, `MapFieldsAsync`, after the caller mask). A secure source maps only when one of these holds:

- (a) the create is secure from creation (`request.SecureFromCreation`);
- (b) the source is the caller's own filing parent: the regarding pair or typed parent resolved from the caller's payload, or an explicit source equal to a parent the caller's row is filed under (or to the pair id it sends). A mapped lookup never counts.

Otherwise the create maps nothing and carries one warning, code `record_creation.mapping_source_secure`. An answer that cannot be told (no owner read back, a fault) maps nothing too. The create still proceeds. Helpers: `SourceMapsBeforeOwner`, `SourceIsCallersFilingParent`.

**The owner check (the invariant).** It runs after step 7 (the owner) and before step 11 (the create), on every path that mapped a value (`EnforceMappingSourceOwnerAsync`). The copy gate is asked again with the decided owner team as the row owner:

- Allowed: nothing changes.
- Otherwise (secure, or not told — a fault resolving the Secure team included): every mapped value is removed, and so is any adapter default copied from a mapped value. The caller's values and the plain adapter defaults stay, and the same warning code is given. The owner and the filing are never changed.

Two choices, made where the brief said "stop and report" might apply:

- If a removed value files the row (the regarding pair, or a lookup that Repair's own `WouldFileTheRow` treats as filing), steps 6–7 read it. Removing it would leave the owner and filing decided by a value that is no longer written. So the create is **refused** (`CheckFailedCode`, nothing written) instead of stripped. Step 3's narrowing makes this reachable only under (a) or (b) with a mapped filing lookup.
- After a strip, the table's pre-write rules are asked again on the row as it will be written.

**Reads.** The source's owner and secure flag (`MappingSourceCopyGate.SourceGateColumns`) are read in the same app-only read as the mapped columns: one read, no new round trip. The caller mask still keeps only the mapped columns, so the gate columns are never copied. A Default-only profile now makes that one read (gate columns only).

**Wizard (preview only).** When the Matter chosen in "Associate To" is secure (`sprk_issecure === true`), the Project wizard turns Secure on and locks it, with a short reason (`SECURE_PARENT_LOCK_REASON`). A non-secure matter, an account, or a failed read leaves it editable. Only the latest association's answer is applied. Files: `CreateProjectWizard.tsx` (`onAssociationChange`), `CreateProjectStep.tsx` (`parentSecure`), `SecureProjectSection.tsx` (`lockedOn`).

**Tests.**

- Unit (`tests/unit/domain/RecordCreation/MappingSourceRuleTests.cs`): (a)/(b)/neither/unknown, and which origins count as the filing parent.
- Seam (`RecordCreationServiceMappingSeamTests.MappingSourceRule.cs`):
  - must fire: an explicit secure source the row is not filed under maps nothing; the Project wizard's create from a secure matter with Secure off maps nothing; a source whose owner does not come back maps nothing; a secure source mapping a filing lookup, not the filing parent, maps and files nothing; a forced owner mismatch under (b) strips every mapped value and keeps the owner, the filing and the caller's values; a fault resolving the Secure team strips.
  - must not fire: a child filed under a secure matter (inferred, and named explicitly) maps and is owned by the Secure team; a non-secure source is unchanged; the Project wizard's create from an ordinary matter maps.
- Existing suites (Office quick-create, the Office to-do, the event/analysis routes, the secure-root create route, the child-record route): the fixture sources now state an owner and a secure flag, as Dataverse returns them.
- Wizard (`CreateProjectWizard.secureParent.test.tsx`): a secure matter locks Secure on with the reason; a non-secure matter, an unreadable one and an account leave it editable; the locked section reports no change.

**Not covered by a test:** the refusal when a removed mapped value files the row. The seam harness has no profile that maps a filing lookup from a secure parent the caller files under.

**Evidence.**

- Fail-then-pass, BFF. The new tests were run over HEAD's `RecordCreationService.cs` and `.Mapping.cs`, shimmed only for the new names (`SourceMapsBeforeOwner` and `SourceIsCallersFilingParent` answer `true`; the constants are present). Result: **12 failed / 9 passed** of 21. The 9 that passed are the must-not-fire cases and the unit rows that expect `true`. With the change: 21 / 21.
- Fail-then-pass, wizard. `CreateProjectWizard.secureParent.test.tsx` over HEAD's `CreateProjectWizard.tsx` and `CreateProjectStep.tsx`: **4 failed / 2 passed**. With the change: 6 / 6.
- Filtered BFF run (RecordCreation, ChildRecord, OfficeQuickCreate, MappingWriteGates, OfficeTodoCreate, AnalysisEndpoints, RecordCreatorPerson, InvoiceReviewWritePath, SecureRootCreateRoute, EventEndpoints, FieldMappingPush, MappingSourceRule, OfficeTodoRegarding): **1756 passed, 0 failed**, 3 skipped (1759).
- BFF test-project build: 0 warnings, 0 errors.
- ArchTests, RecordCreation-related guards: 161 / 162. The one failure is the 045a guard 1 on `CommunicationService.cs:3023` (`CopyRegardingFromSourceAsync`). That file is not touched here, so the failure is pre-existing on the work branch (see found in passing).
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- Jest (`CreateProjectWizard`, `CreateRecordWizard`): **236 / 236 in 17 suites**. `tsc --noEmit`: exit 0. Vite build of `src/solutions/CreateProjectWizard`: `dist/index.html` 1,176.27 kB (319.10 kB gzip), exit 0, not deployed.

**Found in passing (fix round 2).** ArchTest `RegardingPairTypeImpliesIdGuardTests` (045a guard 1) fails at the work-branch tip: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs:3023`. `CopyRegardingFromSourceAsync` writes `sprk_regardingrecordtype` with no `sprk_regardingrecordid` in the same member. Not changed here.

## Held: Restricted sources

Held for a follow-up. In rule wording only:

- A Restricted source (`sprk_accesspermission` = Restricted) follows the same class: values mapped from it are written only into a row that ends Restricted under the same source.
- It needs two predicates beyond the secure one:
  - per source table, whether the source is Restricted (the column and the tables that carry it);
  - per target, whether the row being created ends Restricted in this create.
- `sprk_document` gets its access permission after the create (#2169), so the second predicate cannot be read off the row before the create for a document target.

## Fix round 3 (the strip's branches; the refusals' words, 2026-11-09)

**The strip (`EnforceMappingSourceOwnerAsync`, `RecordCreationService.Mapping.cs`).**

- A copied default is removed with the strip when its source column was mapped at step 3. The service records step 3's mapped columns before step 5 (`stepThreeMappedColumns`) and matches each copy's source against them, so a copy is removed even when step 5 dropped its source and kept the copy.
- A removed value that files the row refuses the create with its own code, `record_creation.mapping_source_secure_filing` (400, `RecordCreationRefusalKind.Invalid`): "This {noun} can't be created with values copied from a secure record, because it would not be secure. Create it as secure, or file it under a different record." The same request is refused every time, so the words never suggest retrying. The child-record route renders it through the shared map with that `reasonCode`; `webApiCreateError.ts` maps it to the same sentence.
- **Fail closed, intended.** If the strip's rebuild (`DataverseWriteItemMapper.MapAsync` over the caller's values and the kept defaults) cannot produce a row, or faults, or the pre-write rules' re-run faults, the whole create is refused and nothing is written. A rebuild that produces no row keeps `record_creation.check_failed` (503): it is a check that could not be completed.
- The step 3 and strip warnings (`record_creation.mapping_source_secure`) now say that some fields weren't copied from the related record because it is secure.

**Secure matter, no Share.** A project under a secure matter is secure (the wizard locks Secure on), so creating one needs the rights a secure create needs: Write and the table's Share privilege. A caller without them is refused (intended) by the route with the delegation rule's own codes, `sdap.access.deny.delegation_share_required` / `delegation_write_required` (403, ProblemDetails with `reasonCode`). With the matter secure, the wizard passes it to `ProjectService.createProject` (`parentSecure`), and `describeWebApiCreateError` (`secureParent: 'matter'`) says: "Creating a project under a secure matter needs permission to share (or edit and share) secure records. Ask an administrator." It never offers "create it without Secure", which the locked toggle does not allow. The check-failed sentence under a secure matter drops that offer too. With no secure matter the sentences are unchanged.

**Wizard known limits (preview only; the server rule is unchanged by them).**

- K: the preview reads `sprk_issecure` as the user. The column is field-secured, so a user without field Read on it gets `null`: no lock, Secure stays editable. The server still applies the mapping-source rule (step 3 maps nothing, or the strip removes what was mapped).
- K: a locked toggle forces `secure=true`, which needs Write and Share (above). A user without them cannot create a project under a secure matter from the wizard; the refusal names the rule and whom to ask.

**Tests (seam, `RecordCreationServiceMappingSeamTests.MappingSourceRule.cs`, a work assignment filed under the secure matter, owner forced to an ordinary team with `ForcedOwnerResolver`, the profile a Copy rule for Assigned To Internal and a Default rule for the description):**

- the default copied from a mapped value (`sprk_assignedto` from `sprk_assignedtointernal`) is removed, with the Default rule's value;
- a plain adapter default survives the strip through the rebuild (`kept.Count > 0`);
- the copy is removed when step 5 dropped its mapped source (`sprk_assignedtointernal` field-secured for the caller);
- a removed value that files the row (`sprk_regardingproject`, mapped) refuses with `record_creation.mapping_source_secure_filing`, 400, words with no retry, nothing written;
- the pre-write rules are asked again on the stripped row, and the re-run's refusal stands.

Client: `webApiCreateError.test.ts` (the filing code's sentence; the secure-matter Share/Write sentences; the check-failed sentence), `secureFromCreation.test.ts` (`createProject` with and without the secure matter).

**Evidence.**

- Fail-then-pass, strip branches. Each branch broken in turn in `RecordCreationService.Mapping.cs`, the five strip tests run:
  - copy removal off: 3 failed (copy, rebuild, step-5 copy);
  - rebuild off (`kept.Count > 0` skipped): 1 failed (rebuild);
  - copy matched on the post-step-5 columns (the round 2 rule): 1 failed (step-5 copy);
  - filing refusal off: 1 failed (filing);
  - pre-write re-run off: 1 failed (pre-write);
  - the round 2 refusal (503 `check_failed`): 1 failed (filing).
  With the change: 5 / 5.
- Fail-then-pass, client. Over HEAD's `webApiCreateError.ts`, `projectService.ts` and `CreateProjectWizard.tsx`: 5 failed / 54 passed (2 suites). With the change: jest on `src/utils/__tests__/webApiCreateError.test.ts` and `src/components/CreateProjectWizard`: 275 / 275 in 14 suites. `tsc --noEmit`: exit 0.
- Office quick-create's refusal map (`OfficeService.MapQuickCreateRefusal`) passes `record_creation.mapping_source_secure_filing` through at 400 with the service's words, never the generic 500. `OfficeQuickCreateRefusalMapTests` has its row (the ledger test failed on the new code until it was mapped).

**Not in this round.** The field-mapping push gate (the coordinator's item 6: `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` and its preview asking `MappingSourceCopyGate.DecideAsync` per child, after merging `task/fm-045b-2`) and the sweep for other server paths that copy field values between records are a separate deliverable. The rule they enforce is this task's: values from a secure source are written only into a row owned by that source's isolation owner.
- Filtered BFF run (RecordCreation, ChildRecord, OfficeQuickCreate, MappingWriteGates, OfficeTodoCreate, AnalysisEndpoints, RecordCreatorPerson, InvoiceReviewWritePath, SecureRootCreateRoute, EventEndpoints, FieldMappingPush, MappingSourceRule, OfficeTodoRegarding): **1762 passed, 0 failed**, 3 skipped (1765). Before the Office row was added: 1 failed (`OfficeQuickCreateRefusalMapTests.EveryCodeTheServiceDeclares_HasARow`).
- BFF build: 0 warnings, 0 errors. BFF test-project build: 0 warnings, 0 errors.
- `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api): no vulnerable packages.

rework: fix-round-3 task 022 (F3 strip-branch tests; K4 copies matched on step 3; K1 filing refusal code and words; fail-closed rebuild noted; wizard Ks noted; secure-source and secure-matter refusal words)
