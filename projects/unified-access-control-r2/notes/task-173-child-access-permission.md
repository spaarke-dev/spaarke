# Task 173 — child records show their parent's Access Permission

> Owner rounds 81 and 84 (2026-10-08), GitHub #1423. POML: `tasks/173-child-records-show-parent-access-permission.poml`.
> Branch `task/uac-r2-173` (worktree `C:\wt173`, from `origin/master` fc639d2c1).

## 1. What was built

| Part | Where |
|---|---|
| The rule, once: walk a row's filing lookups to the records at the top; most restrictive `sprk_accesspermission` (null = Standard); no parent → no inherited value | `Services/Dataverse/ParentLineage.cs` (`ParentLineage`, `ParentLineageWalk`, `InheritedAccessPermission`) |
| Shared stamp path (L1) | `CoreAncestorResolver.StampAsync` (both overloads) writes the value into the payload; `DeriveForHostAsync` returns it (`CoreAncestorStampOutcome.InheritedAccessPermission`) for its three callers; `ResolveInheritedAccessPermissionAsync` (an existing row's update); `RefreshInheritedAccessPermissionAsync` (read the stored row, write when it differs) |
| Callers that emit their own payload | `EventEndpoints` → `CreateEventRequest.AccessPermission` → `DataverseWebApiService.AddRegardingWriteSet`; `EmailDraftToolHandler.BuildCommunicationItem`; `IncomingAssociationResolver.ApplyCoreAncestorStampsAsync`; `ChildRecordEndpoints` create + re-file (through `CoreAncestorRestamper.RefreshInheritedAccessPermissionAsync`) |
| Reconcile (L4) | `Services/Access/ChildAccessPermissionReconciler.cs`, run by `SecureChildReconciliationJob` every 2 minutes with its OWN watermark, carried rows and per-instance sweep cursor |
| Form lock | `src/client/webresources/js/sprk_accesspermission_inherited.js`; `scripts/Set-InheritedAccessPermissionFormLock.ps1` (+ fixtures `tests/fixtures/form-access-permission-lock/`) |
| `sprk_document` column in source | `scripts/Set-DocumentAccessPermissionSchema.ps1`; `docs/data-model/child-access-permission.md` |
| Docs (goal 7) | `docs/data-model/sprk_communication.md`, `unified-access-control-cascade.md:37`, `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`, `ExternalParticipationService.cs` remarks, `EmailWorkspace.mapping.ts`, `TrackingFieldTrio/index.ts`, `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (new invariant I-16), `scripts/README.md`; `scripts/Retire-CommunicationAccessPermission.ps1` deleted |

## 2. Live facts (step 1, read-only, spaarkedev1 2026-10-08)

- `sprk_accesspermission` exists on `sprk_todo` (`43b1ce4c…`), `sprk_event` (`038b50db…`), `sprk_communication` (`bceb6389…`), `sprk_document` (`4b469c31…`), and the three roots. **All seven are bound to ONE global choice `sprk_accesspermission` (`184fab40-6d75-f111-ab0e-7ced8ddc4a05`)**, options 100000000 Standard / 100000001 Limited / 100000002 Restricted, default Standard. Escalation trigger 1 does not fire.
- `sprk_document` is in SpaarkeCore with rootcomponentbehavior 0, so the column ships with it (`Set-DocumentAccessPermissionSchema.ps1 -Verify` PASS). The global choice is in SpaarkeMaster and Default only (informational; the roots' columns have the same dependency).
- Forms that SHOW the column: only the **To Do main form** (`eca59df4`) and the **Event main form** (`eaf22dcb`), both through the TrackingFieldTrio `accessPermission` parameter. No Communication or Document form shows it.
- Data: 573 documents, 571 with a null value (column added live, not backfilled); 296 communications, 107 in a thread, 61 of those with no root regarding; 6 documents with `sprk_currentversionid`; 0 with `sprk_relatedtodo`.

## 3. Create / re-file path inventory (step 2)

Mechanism: **L1** = inline in the write; **L4** = `ChildAccessPermissionReconciler` within one 2-minute run (a write changes `modifiedon`).

| Table | Path | Mechanism |
|---|---|---|
| To Do | `POST /api/office/todo` (`OfficeService.CreateTodoAsync`, record regarding and/or carrier) | L1 (`StampAsync`) |
| To Do | AI task action (`TaskActionCore.CreateAsync`) | L1 (`StampAsync`) |
| To Do | Generated to-dos (`TodoGenerationService`, from events) through `TodoRegardingBuilder.ApplyResolverFieldsAsync` | L1 (`StampAsync`) |
| To Do, Event, Document (+ memo, invoice, …) | `POST /api/v1/child-records/{table}` (wizards, BFF-backed form commands, task 147) | L1 (`RefreshInheritedAccessPermissionAsync` after the create) |
| To Do | `PATCH /api/v1/child-records/sprk_todo/{id}` (re-file, or the caller's own update of the column) | L1 (refresh after the write) |
| Event | `POST /api/v1/events` (`EventEndpoints`, `DeriveForHostAsync`) | L1 (create body) |
| Communication | `CommunicationService` send / record (`StampAsync`) | L1 |
| Communication | AI email draft (`EmailDraftToolHandler`, `DeriveForHostAsync`) | L1 |
| Communication | Inbound association (`IncomingAssociationResolver.ApplyDecisionAsync`, an UPDATE of the stored row: stored filing ∪ the decision's) | L1 |
| Communication | Inbound create (`IncomingCommunicationProcessor.CreateCommunicationRecordAsync`), email upload capture, messaging ingestor, Compose promote | L4 (the association update above follows for inbound mail) |
| Document | Every document create (`DataverseServiceClientImpl.CreateDocumentAsync` seam: `POST /api/v1/documents`, Office save, upload finalization, email attachment processing, attachment materializer, Compose) and `PUT /api/v1/documents/{id}` | L4 (documents carry no stamp; none goes through `StampAsync`) |
| To Do / Event / Document | External portal creates (`ExternalDataService.Build*CreatePayload`) | L4 |
| To Do / Event / Communication | The AI create-record tool (`DataverseCreateRecordHandler` → `OwnedChildWrite.CreateAsync`) | L4 |
| Event / Communication | Filing routes `PATCH /api/v1/events/{id}/filing`, `PATCH /api/communications/{id}/filing`; field-mapping push; AI update tool | L4 |
| All four | Model-driven forms, quick create, grid edit, `Xrm.WebApi`, import, flow, direct Web API | L4 |
| All four | A parent's value changes (a matter turns Restricted), at any depth ≤ 6 | L4 (downward walk from every changed row) |
| All four | Rows nobody changed since before task 173 (the 571 null documents) | L4 sweep window (per instance, `MaxAccessPermissionRowsPerRun`, default 1000) |

Escalation trigger 3 does not fire: no path writes these tables without the reconcile seeing the row within its cycle (every write changes `modifiedon`, and the listing covers all four tables and every table above them).

## 4. Decisions made in the task

1. **Filing lookups = `SecureChildLineage` minus `sprk_document.sprk_currentversionid`** (`ParentLineage.NotFiling`). The current version names the document's own file version, whose `sprk_document` names the document again: with it, every document with a file would read as "filed" and never parentless. Pinned by `ParentLineageTests.ChildFiling_IsTheLineageMap_WithoutADocumentsOwnCurrentVersion`.
2. **The form decides "has a parent" from the server's map, not from `config/regarding-filing-columns.json`.** Deviation from goal (5)'s wording, for goal (1)'s rule: the regarding list includes party lookups (`sprk_regardingcontact`, organization, account) and the service request, which are NOT parents under the lineage — a To Do regarding only a contact is parentless (its own value), so locking it would leave a field nobody can set and the server never fills. That list also has no entry for `sprk_document`. The form library carries the server map literally, pinned by `ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap`.
3. **Roots are terminal in task 173**: a To Do under a work assignment takes the work assignment's OWN value. Tasks 174/175 make the work assignment's stored value follow its parent; the reconcile then descends from it (a changed row) to its children. `ParentLineageWalk` takes the filing map and the value columns as parameters, so 174/175 pass a map with the roots' own filing lookups and `[sprk_issecure, sprk_accesspermission]`.
4. **A top whose table has no `sprk_accesspermission` counts as Standard** (goal 1, "a null counts as Standard"): a To Do under an unfiled invoice is Standard and locked; a communication in an unfiled thread is Standard and locked (owner question O-1 below).
5. **A filing that loops without reaching a record that has no parent is Undetermined**, never "each other's value" (that would swap two rows' values every run).
6. **The reconcile has its own watermark, listing and carried rows** (not the secure pass's): a fault in it must never hold the secure pass's window (#1378, "do not widen it"). The per-table "one try" listing shape is kept.
7. **Undetermined rows do not fail the run** (they are reported and carried, at most `MaxCarriedRows`); a listing fault or a failed write does. The value is display only, and a structural undeterminable row (a loop) would otherwise fail every run.
8. **Not in `CoreAncestorRestamper.AfterWriteAsync`.** That cascade is also the AI update tool's narrow app-only step (`CoreAncestorAfterWriteRestamp`, owner round 8 item 1: stamp columns only). The refresh is a separate member that `ChildRecordEndpoints` calls.
9. **Plain control added to two forms**: Communication "Message main form" (`b58ec3d8`) and "Document main form" (`9088d6a4`) — round 81 "a permission control on these child records, but NOT TrackingFieldTrio on Communication" (owner question O-2: which forms).
11. **Verifier pass 1 (PR #1458) fixes:**
    - F3: a per-run write cap, `SecureChild:Reconciliation:MaxAccessPermissionWritesPerRun`, default 500. A capped run keeps its watermark and sweep cursor and reports `deferred` / `writeCapReached`. The pass now runs AFTER the secure pass saves its cursor and watermark.
    - `CoreAncestorResolver` is required, not optional, in the job; every job test harness registers it (`SecureChildShareWorld.CoreAncestorsOver`).
    - The Office To Do with a record AND a carrier is recomputed after the carrier is set (`CoreAncestorResolver.ApplyInheritedAccessPermissionAsync`); this was the K2 in §8.
    - The remaining "retired" comments in Communication.Components and TrackingFieldTrio are corrected.
    - The restamper doc comment is moved.
    - **ADR-006 — resolved by amendment 2.1 (owner round 86, CLAUDE.md §6.5 path B, PR #1462, merges before #1458).** Thin form-event scripts are allowed within its limits. `sprk_accesspermission_inherited.js` was checked against each:
      - platform form APIs only (formContext, `Xrm.WebApi.retrieveRecord`; no BFF call);
      - no UI of its own (the platform's form notification and control state);
      - no access decision (the server writes the value; the script only locks and labels);
      - fails safe (a failed read leaves the lookups on the form deciding; the reconcile reverts any edit that slips through);
      - namespaced (`Spaarke.AccessPermissionInherited`) and idempotent — made so in this pass: one wiring per form load and record, jest-tested;
      - jest-tested (9 tests, the real script in jsdom);
      - registered by a checked-in operator script (`Set-InheritedAccessPermissionFormLock.ps1`);
      - "a value the script relies on must come from, or be pinned by a test to, the server's rule": `PARENT_LOOKUPS` is pinned by `ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap`.
    - **Goal (5) deviation, recorded:** the lock uses the server lineage map (`ParentLineage.ChildFiling`), not `config/regarding-filing-columns.json` — see item 2 for why.
10. **Locked = disabled + not submitted + a bound PCF's change put back.** A form script cannot disable one parameter of the TrackingFieldTrio pill; the library reverts a change the pill makes while the record has a parent.

## 5. No access decision reads a child's value (AC 7, escalation trigger 2)

`grep -rn "sprk_accesspermission" src/server` (2026-10-08) — every read used for an access decision is on a ROOT:
- `SpeContainerMembershipSync.IsRestrictedAsync` — `entity` iterates the securable tables (those carrying `sprk_issecure`: the roots).
- `OfficeEditAccessService.IsRestrictedOrUnreadableAsync` — `OwningSecureRecord` from `RecordContainerResolver.ResolveOwningRecordAsync`, over `ISecurableEntityRegistry` (roots).
- `ProvisionProjectEndpoint`, `SecureRecordRoot`, `AssignedAccessStore.ScanRestrictedRootsAsync`, `ExternalParticipationService.RootFlagColumns`, `SecureChildShareSynchronizer` (~1019, `RestrictedExternalPrincipalsOrThrowAsync` reads the ROOT), `AccessibleRecordSetService` slot 2 — roots.
- New code reads child values only to compare with the inherited value before writing (`ChildAccessPermissionReconciler`, `RefreshInheritedAccessPermissionAsync`) and to read a parentless TOP's own value as the inherited value (display). No decision.
- Client: `TrackingFieldTrio` maps the bound record's value into the Manage Access modal's state (UI only; the server decides). On a To Do / Event that value is now the parent's, so the modal's state matches the parent. `sprk_access_ribbon.js` reads it on root forms only.

## 6. CLAUDE.md §10 / §11

**Placement.** BFF: the shared child write path and the existing 2-minute job (ADR-052: BFF identity, low volume, the same invariant owner for L1 and L4). No endpoint, no package, no DI registration (the walk and the reconciler are constructed per operation from services already registered), no interface, no new job or timer (ADR-036: extends `SecureChildReconciliationJob`).

**§11 — new surface:**
- `ParentLineage.cs` — Existing: the synchronizer's upward walk (secure rows only, answers "which secure roots"), `CoreAncestorResolver` (one hop). Extension: neither can walk a filing of any owner to its top and read values; the walk is one class both L1 and L4 call. Cost of nothing: the UAT defect (a To Do under a Restricted matter shows Standard).
- `ChildAccessPermissionReconciler.cs` — Existing: the job's secure recent-changes pass (ownership only, skips Secure-team rows), the stamp reconciliation job (stamps only). Extension: it IS an extension of `SecureChildReconciliationJob`, kept in its own class so its listing and watermark cannot stall the secure pass (#1378). Cost of nothing: re-files outside the BFF, parent changes, hand edits and every pre-173 row keep a value the parent does not hold.
- `CoreAncestorResolver.ResolveInheritedAccessPermissionAsync` / `RefreshInheritedAccessPermissionAsync`, `CoreAncestorRestamper.RefreshInheritedAccessPermissionAsync`, `CoreAncestorStampOutcome.InheritedAccessPermission`, `CreateEventRequest.AccessPermission` — extensions of the existing stamp path's surface; without them the three payload-building callers and the client create route could not write the value.
- `sprk_accesspermission_inherited.js` — Existing: `sprk_todo_regarding_presave` (on To Do / Event / Communication / Analysis forms, not on Document; its contract is transcribing the resolver payload). Extension rejected: it would put an access-permission UI rule into the presave and still not reach Document forms. Cost of nothing: the field stays editable on a parented record and the reconcile silently reverts the user's edit.
- `Set-InheritedAccessPermissionFormLock.ps1` — Existing: `Add-RegardingFilingPickerToForms.ps1` / `Lock-CoreAncestorStampColumnsOnForms.ps1` (their transforms are specific to the regarding columns). Its transform helpers are copied from them (the established per-script shape). Cost of nothing: the forms are hand-edited with no snapshot, restore or verify.
- `Set-DocumentAccessPermissionSchema.ps1` — Existing: none for this column. Cost of nothing: no other environment gets `sprk_document.sprk_accesspermission`.

## 6a. Publish size, CVEs (CLAUDE.md §10, NFR-06)

| | master `6cd5d9a47` (`C:\wt173m`) | branch `1b61a194b` (`C:\wt173b`, master merged) |
|---|---|---|
| Files | 192 | 192 |
| Zip | 38,011,535 B (36.25 MB) | 38,042,928 B (36.28 MB) |

Delta **+31,393 B (+0.03 MB)**. Re-measured after verifier pass 1 (branch `8a7fd1f0c` vs master `b0a78f880`, whose new commits touch no server code): 38,044,388 B vs 38,011,524 B, **+32,864 B (+0.03 MB)**, 192 files each, same convention. Convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent linux-x64, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included (4). Both from fresh short-path worktrees. `dotnet list package --vulnerable --include-transitive`: no vulnerable packages (no package added).

## 7. Tests

Scope (AC 10): the rule (ranking, null, unknown value), the walk (top of a deep filing, two roots, parentless top, contact not a parent, unreadable parent, too deep, loop, a document's own version), the stamp path (`StampAsync`, `DeriveForHostAsync`, the association update, the refresh), the owner's UAT through `POST /api/office/todo`, the event create body, the reconcile (parent change at depth, hand edit, correct rows not written, parentless and un-filed rows never written, unreadable parent left and carried, sweep backfill page by page), the form-lock script (`-SelfTest`, 33 checks), the form/server map parity.

- `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/ParentLineageTests.cs`
- `tests/integration/data-mutation/ChildAccessPermission/InheritedAccessPermissionStampTests.cs`
- `tests/integration/data-mutation/ChildAccessPermission/ChildAccessPermissionReconcileTests.cs`
- `tests/integration/contract/Api/Office/OfficeTodoRegardingContractTests.cs` (+2: the UAT; an unreadable matter never fails the create)
- `tests/integration/contract/Api/Events/EventEndpointsAuthorizationContractTests.cs` (+1: the create body)
- `tests/integration/data-mutation/ExternalAccess/SecureChildShareWorld.cs` (the in-memory Dataverse models the one-column write, `GreaterThan` on an id, and an id-list read fault)
- `src/client/shared/Spaarke.UI.Components/src/__tests__/accessPermissionInherited.test.ts` (the REAL form library in jsdom: lock / unlock / revert / saved parents / read failure; 8 tests)

**Suite results (branch merged with master `6cd5d9a47`, 2026-10-08):** `Sprk.Bff.Api.Tests` 18,760 passed / 4 failed / 54 skipped in a 33-minute run — the 4 (`DocumentEmailIdentityContractTests`, `DocumentProfileContractTests`, `ChatAgentRouteProofTests`, the upload-session binding verifier test) each ran ~3 minutes (host start-up timeouts under load) and all pass on re-run in isolation (22/22); none touches this task's code. `Spaarke.ArchTests` 811/811. `Sprk.Bff.Api.IntegrationTests` 87 passed / 4 skipped. `Spe.Integration.Tests` 350 passed / 25 skipped. Task 173's own: 30 .NET (data-mutation + unit) + 3 contract additions, 8 jest, script `-SelfTest` 33 + 9.

**Pass-1 fixes (commit c469c4054):** full `Sprk.Bff.Api.Tests` 18,766 passed / 0 failed / 54 skipped; ArchTests 811/811; focused suites 341/341 (task tests, every job harness, Office and Event contracts, child-record routes). The record+carrier Office test goes red when the recompute is removed.

**Seeding proof (the one the AC asks for)**, run 2026-10-08: in `ChildAccessPermissionReconciler.RunAsync`, replacing the `parents.Count == 0` branch with "a parentless row inherits Standard" turned `Run_AParentlessChildsOwnValue_AndAnUnfiledChildsLastValue_AreNeverOverwritten` red on its write assertion; dropping the branch alone turned it red on `parentless`. Restored; green.

Beyond the AC's list, one line each: the event-create and email-draft payload cases prove the two `DeriveForHostAsync` callers emit the value (goal 2 names those paths); the parity test is the only guard on the form/server map; the world extensions are test infrastructure.

## 8. Known limits

- **K2** — a child created or re-filed off the L1 paths (§3 "L4") shows its old value for up to one 2-minute cycle; display only, access is unaffected.
- **K2** — a filing that cannot be decided (unreadable, missing, a loop, deeper than 6) keeps its stored value and is logged / carried; display only.
- **K2** — after a restart the new instance's window starts 60 minutes back; older drift is reached by the sweep window, ceil(rows / 1000) runs.
- **K4** — the `/api/v1/child-records` route's inline refresh is covered by the helper's own tests and backstopped by the reconcile, not by a route-level test (the route harness creates rows in a different in-memory world than its restamper reads).
- **K2** — in an environment that lacks one of the 13 tables the pass lists (or a lineage lookup column), the listing fails every run and reports it (the run is unsuccessful; nothing is written wrongly). Same root cause and fix candidates as the secure pass's #1378; the pass has its own watermark, so it never stalls the secure pass.

## 9. Supersession (for `notes/decisions.md` "Superseded and withdrawn rules", on the work branch)

> **2026-10-08 — round 81 / task 173 supersedes task 138's retirement of `sprk_communication.sprk_accesspermission`** (round 2 Q6's "a communication has no Access Permission of its own; its copy is retired"). The column is kept on To Do, Event, Communication and Document as a DISPLAY copy of the parent's value (most restrictive across parents), written by the BFF and locked on the form while a parent exists; a parentless record keeps its own (recorded only). Q6's ACCESS rule stands: no access decision reads a child's copy. `scripts/Retire-CommunicationAccessPermission.ps1` is deleted (running it would destroy the column). Docs corrected: `docs/data-model/sprk_communication.md`, `unified-access-control-cascade.md`, `EXTERNAL-ACCESS-ADMIN-SETUP.md`.

(`notes/decisions.md` exists only on `work/unified-access-control-r2`; this PR is against master, so the main session adds the entry there.)

## 10. Owner questions

- **O-1** — a communication in a thread that is filed under nothing (61 in dev) counts as filed (the thread is a lineage parent) and shows Standard, locked. Recommended: keep (the thread is what files a message; a thread filed later carries its filing to the message). Alternative: treat a thread with no filing of its own as "no parent" (the message keeps its own value, editable).
- **O-2** — the plain control was placed on the Communication "Message main form" and the "Document main form" only (the Communication "Information" main form and the Document "Information" and Quick Create forms do not get one). Recommended: these two. `-AddControlForms` takes any list.

## 11. Deploy order (main session)

1. Merge; BFF deploy from a fresh short-path worktree of `origin/master`: `pwsh -File scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev`.
2. `pwsh -File scripts/Set-DocumentAccessPermissionSchema.ps1 -Verify` (PASS today on dev; `-Apply` only in an environment where it fails).
3. Web resource: `pwsh -File scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_accesspermission_inherited -FilePath src/client/webresources/js/sprk_accesspermission_inherited.js -WebResourceType 3`.
4. Forms (no other formxml writer running): `Set-InheritedAccessPermissionFormLock.ps1` dry run → `-Apply` (snapshot) → `-Verify` (checks the deployed library byte for byte too).
5. Watch the next two `secure-child-reconciliation` runs: `ResultJson.accessPermission` (`changed` > 0 on the first runs — the backfill — then ~0; `failure` null).

## 12. Live gates

- G-1 (AC 12, the owner's UAT): a To Do created through the Word web add-in on a document under PAT-176903 shows Restricted, locked, with "Access permission is inherited from …".
- G-2: change a test matter Standard → Restricted; within one run its To Do / Event / Communication / Document children show Restricted; set one child back to Standard by hand (grid edit): reverted within one run.
- G-3: a To Do with no regarding, set to Limited: editable, still Limited after two runs.
- G-4: the Communication Message form and the Document main form show the field, locked on a filed record, editable on an unfiled one; no console errors; dark mode readable.
