# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-24, starting task 026. Desktop session `field-mapping-r1`.
> Previous version (055/056 build, deploy and live checks): `notes/handoff-history/current-task-2026-11-23-pre-024.md`.
> State only here; decisions in `notes/decisions.md`, standing rules in the project `CLAUDE.md`.

## Quick Recovery

| Field | Value |
|---|---|
| **Session / worktree** | `field-mapping-r1`, `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`, on `work/field-mapping-server-write-path-r1`. |
| **Work branch** | 025 closure (accd2a4d37) + the 026 merge (task/fm-026 9b60222577) + this notes commit; not pushed. Origin at 0c3f397d9b; master has #3031 (57a68b06c5) and #3041 (2566d561e0). |
| **Dev** | BFF 996928732e (UAC-r2, 2026-11-24 03:01Z). `sprk_createtodowizard` from master 2566d561e0 (03:46Z, SHA-256 AD2955B3…1B50). `RecordRepair:Pass:GoLive` unset everywhere. |
| **Active task** | **026** Work Assignment wizard (FR-16), FULL rigor, sonnet @ high, STEP MODE directional. Code merged on the work branch 2026-11-24; review (no finding) + gates pass (notes/task-026-notes.md "Main session"). Left: PR to master, deploy `sprk_createworkassignmentwizard`, live check: each on the owner's go. |
| **026 findings so far** | `workAssignmentService.ts`: `applyFieldMappings` import :38, call ~:447 (comment ~:445); WA create via `createRecordViaBffWithWarnings` ~:502 (a root, ontology 046); follow-on `sprk_event` via `this._dataService.createRecord` ~:669 (wrapped by `withBffChildWrites` :106, so it already goes to the BFF route, but with no mapping source). Follow-on event Assigned To already binds by exact column (comment ~:650-655). `ChildRecordEndpoints.InferMappingSourceTables` = {sprk_matter, sprk_project} only (:102). **Prefer an explicit `mappingSource` from the client for the follow-on event (client-only) over adding `sprk_event` to InferMappingSourceTables (a BFF change needing publish delta and an exact-SHA owner go).** Dev profiles: "Matter to Work Assignment (Attorney Matrix)" 7b147316-7f96-f111-b8db-3833c5e5d030 active; "Matter to Event (Attorney Matrix)" 24dc0ed2-537b-f111-ab0e-7ced8ddc4a05 active. |
| **026 approach** | As 024: one Sonnet implementer in an `isolation: worktree` agent, first command `git switch -c task/fm-026 <work-branch tip>` (gotcha 2026-10-20); copy `CreateTodoWizard/todoService.ts` + `__tests__/todoService.fieldMapping.test.ts` (024) and `CreateEventWizard/eventService.ts` (023). This session reviews, runs gates, merges `--no-ff`; then master PR, deploy `sprk_createworkassignmentwizard`, live check (incl. a secure Matter) — each on the owner's go. |
| **Uncommitted** | None. |
| **Next action** | 026 on master via #3049 (dc33f14d35); `sprk_createworkassignmentwizard` live on dev from master d0453e583a (15:37Z, SHA E8DE9527…4B60A1; rollback saved). **Live check DONE 2026-11-24 15:46–15:52Z (owner go)**, logs in scratchpad `live026/` (read-wa.ps1, waA/B/C.log). A = WA 4dbaa0c7-79ec-4c9f-8b3f-6a8eb2479adc + event a9b1aaee-d5dd-473a-a021-cdf5f3a864b6 under MAT-000110: PASS (attorney1 = Jane Smith user value kept over mapped Test User 1; attorney2/para1/para2/int/ext mapped; lawfirm1 slot; owner = matter's team Spaarke Business Unit 1; 4.7 s). B = WA adff04f4-157e-405a-9d15-2bc76b2f976a + event d5d8d35b-0d64-48c3-8adf-62bcc3045bf7 under secure PAT-908068: PASS (Secure Record Owners, issecure true; 24.6 s = UAC-r2's secure provisioning: SPE container 5.5 s + ~90 sharing calls; latency verdict is UAT's). C = WA 9891e505-b71c-4c51-ab92-0270ce9e226b no parent: PASS (owner = maker's BU team Spaarke). **DEFECT found (pre-existing since March, not a 026 regression): the follow-on event writes only `sprk_regardingworkassignment`, no regarding pair (id/url/type/name/number); ADR-024 wants lookup + resolver fields.** FIXING in scope (owner: "fix, not just file"): in `createFollowOnEvent` replace the manual bind with `applyResolverFields(polyWebApi, entity, navProps, 'sprk_workassignment', 'sprk_workassignments', workAssignmentId, workAssignmentName ?? '', 'sprk_regardingworkassignment')`; add optional 3rd param `workAssignmentName`, dialog passes `form.name` (WorkAssignmentWizardDialog.tsx:642); update payload tests (event exact key sets at payload.test.ts ~:330-375 gain the 5 pair keys; makeDataService mock must answer the WA catalog: RT row, number field `sprk_workassignmentnumber`, display `sprk_name`; EVENT_RELATIONSHIPS needs `sprk_regardingrecordtype` → sprk_recordtype_ref) + followOnAssignee test. Then jest/tsc/lint, commit, then ask owner: PR → redeploy → re-check event pair. Then notes (task-026 "Live check" section). |

## 024 (done except the owner's dark-mode look)

- Live check PASS 2026-11-24 incl. the Assignee re-check (notes/task-024-notes.md). Assignee fix via #3041; Event wizard's latent twin filed as DI-039 / #3039.
- Dev test rows: To Dos 7d1cba50-dfd9-4920-8e5e-6842e788cbac, 354ffd82-ade9-4a02-9c4d-ebd39a21ca08, 2c53f9cc-686d-4048-94e2-d95ec694b761; inactive temporary profile 6a6d6cab-b2e7-f111-84ea-7ced8dda31f8 (+2 rules).
- The Smart To Do `action=createTodo` launch and the Console in-app wizard pick up 024 only when `sprk_smarttodo` / `sprk_spaarkeai` are next deployed.

## 025: cancelled 2026-11-24 (owner) as superseded

ebilling-r1 052 moved the invoice wizard to internal submission; invoice mapping is 032b's step in `InvoiceSubmissionPersister`; the wizard live check moves to 032b-3.
## Done 2026-11-23 (for context)

- 055 and 056 ✅: live checks PASS on dev, confirmed by UAC-r2. Test rows on dev: communications c0dfc7b4-8be7-f111-84ea-00224829aef9, cd34edc0-8be7-f111-84ea-00224829aef9. Follow-ups: DI-038 / #3023 (refusal title); #3024 is the messaging owner's.
- 023 live check PASS (events e6a65599-be22-42e0-9f94-a8e744dab467, 4290d1ce-d191-4165-ad8a-1cf8ed848d17 left on dev).
- Chrome: the owner's Chrome is signed in to dev; open a new tab group (ontology-r1 uses its own tab). The saved Spaarke theme there is `light`.

## Binding deploy order for Direct messages (other environments)

Each live step needs the owner's go; on dev every step is done (detail: `notes/task-054-notes.md` "Live runs").

1. BFF from master with the `DirectMessage:*` settings unset (piece 3 off).
2. 054b `Set-ThreadFlagFieldSecurity.ps1`: dry run → `-Apply` → `-Verify` (owner's go in THIS session).
3. Direct setup + the two `DirectMessage:*` settings (owner's go in UAC-r2's session), only after step 2's `-Verify` exits 0; DI-026 (#2701) check first.
4. 054c Stamp: dry run → `-Apply` → `-Verify`.
5. 054c Move (`-DirectShareJobLive`; the first run scoped to one message, L16).

After every SpaarkeMaster import where 054b is applied: `-Verify`, and `-Apply` if it fails. A message moves to the Direct team only with a valid stamped pair.

## Known limits carried

- **022:** K2 (the isolation owner is the single Secure team), K3 (the Project wizard's follow-on creates map in the browser; tasks 023/024), K4 = DI-024 #2648.
- **022b:** K1–K6 in `notes/task-022b-notes.md`.
- **054:** 054a L3, L6–L10; 054b K1–K5; 054c L11–L16 (the `GrantAccess` shape is not exercised live); #2728; #2722.
- **045e K8:** the push never writes a pair, against the `PushMayWrite` / FR-23 comments. It is wording only. Fix the predicate and comment the next time the file is touched; it gets no PR of its own.

## Open owner items

- Hand checks: 023 dark-mode look (Spaarke theme dark); 021 after its deploy; the 076(c) re-run after UAC-r2 deploys 076c.
- Deleting the snapshot branches `…-049c`, `…-030b` and `…-054`.

## Later lanes

023–029 (wizard chain), 035, 036, 050/051, 053, 061–063, 090; DI-031 (#2874).
Cleanup: the merged agent worktrees still listed under `C:\code_files\spaarke\.claude\worktrees\` (task/fm-023, -052c, -052d, -052f, -052g, -052g-r2). Remove them only with `scripts/worktree/Remove-WorktreeSafe.ps1`.

## Coordination

- Peers:
  - `uac-r2` (desktop, `local_54acf7b0-9732-48a1-aa4c-2d840ded57e5`);
  - ebilling-r1, mcp-r1, spa-r4, provisioning-r1, ontology-r1, word-add-in.
- Access defects go to UAC-r2 by message only.
- UAC-r2's access files are not ours to edit. The list is in POML 056 and the project `CLAUDE.md`. For example `DirectThreadAccessService.cs`: we were allowed to edit one condition there (055), and that permission did not carry over to anything else.
- List UAC-r2's access-path files at the top of any diff we send them.
- Office-scope server test legs hit their 20-minute job cap across branches and master (#1474, #1718, #2767). The legs are not required checks. Read the test summary in the log before calling a red leg a failure.
- Nightly quality alerts open on master: #1906 (client tests), #2067 (flake hunt), #2866 (duplication sweep).
