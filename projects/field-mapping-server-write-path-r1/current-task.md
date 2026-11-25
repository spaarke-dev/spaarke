# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-25 (checkpoint at 85% context), task 028 merged and its client pages live on dev; follow-up PR #3148 on auto-merge. Desktop session `field-mapping-r1`.
> Previous version (055/056 build, deploy and live checks): `notes/handoff-history/current-task-2026-11-23-pre-024.md`.
> State only here; decisions in `notes/decisions.md`, standing rules in the project `CLAUDE.md`.

## Quick Recovery

| Field | Value |
|---|---|
| **Session / worktree** | `field-mapping-r1`, `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`, on `work/field-mapping-server-write-path-r1`. |
| **Work branch** | Pushed. Master has #3049, #3067, #3072, #3083 and #3133 (7f13cf2e26, task 028). PR #3148 (Communication record type: seeder + admin guide) open from the work branch, GitHub auto-merge ON (merge commit, owner "merge when green" 2026-11-25); UAC-r2 and provisioning-r1 no objection. |
| **Dev** | BFF ef0c566d2c (UAC-r2; candidates only from master; the owner gives "BFF deploy <sha>" in UAC-r2's session, never here). `sprk_spaarkeai` from master 7f13cf2e26 by the deploy workflow 13:43Z (a later run from 423cfc67f7 includes it). `sprk_playbooklibrary` from 7f13cf2e26 at 13:49Z (SHA B2AA71FB…, rollback C:	mp28-rollback, SHA 6C0C6DBA…). `sprk_createreportcardwizard` from e70ff38894. Dev BFF now a1b7db1def (UAC-r2, gate passed 16:29Z; rollback ef0c566d2c). Catalog row `sprk_communication` 6b59ba77-dee8-f111-84e8-7ced8d1d8f6b (permanent). Temporary profiles 62bac40d… and 4e88e776… deactivated 16:47Z. PAT-176903 is Restricted on dev: use MAT-000113 (Standard) for mapping checks. `RecordRepair:Pass:GoLive` unset. |
| **NOW: task 029** | Document Upload + Summarize Files wizards (FR-16), FULL, sonnet/high, directional. Owner go 2026-11-25 ("Yes, start 029"). Rigor declared; 041 ✅ (no escalation). Work branch merged with master (fbc5992f24, not pushed). Step 1 trace in progress. Found so far: `DocumentRecordService.ts` builds `sprk_searchindexname` (:227, :380) and `sprk_AI_Search_Index@odata.bind` (:232, :386) from args `searchIndexName`/`searchIndexId`; `uploadOrchestrator.ts:346-381` calls `resolveSearchIndexNameForRecord` (re-exported from `components/AssociateToStep.tsx:165`, defined in `components/searchIndexResolver.ts` + its test) and passes it to `createDocuments`; also `searchIndexName` params at :498/:542/:575/:606. **STATE 2026-11-25 late:** agent done; `task/fm-029` (0abfd7abc4 Upload, 02c410e8bb Summarize) merged `--no-ff` into work as daa45b00ff (NOT pushed). Upload payload now name/filename/size/description/parent bind only; parent sent as `mappingSource` query (server infers only matter/project: `ChildRecordEndpoints.InferMappingSourceTables`); rag/index-file body drops searchIndexName; `searchIndexResolver.ts`+test deleted. Summarize → `createChildRecordViaBff('sprk_document',{sprk_documentname})`, no BU container read. Route accepts parentless docs (ChildRecordEndpoints.cs:78, RecordTableRules.cs:347-364, RecordCreationService.cs:1363-1380). Tests: shared 53/53, DocumentUploadWizard 22/22, builds OK, suppressions SummarizeAnalysisStep 5→2. Agent-reported defects: (D1, in scope?) `withBffChildCreates` has no deleteRecord so `DocumentRecordService.createAndAttach` cleanup never runs → orphan file-less row when attach fails; (D2, pre-existing) Summarize creates file-less sprk_document rows (never uploads bytes/attach) then hands ids to playbook library — verify impact, file + report. NEXT: Step 9.5 — one adversarial-reviewer pass on `git diff e3c9943c93..daa45b00ff`; decide D1 fix; file D2; push; PR; then deploy+live check on owner's go. Agent worktree `C:\code_files\spaarke\.claude\worktrees\agent-a7cd01ef65e06ec54` (remove with Remove-WorktreeSafe.ps1 after merge to master). Earlier: **Owner 2026-11-25: option B (move Summarize too).** Implementer agent (sonnet, isolated worktree) launched on branch `task/fm-029` from e3c9943c93 (pushed): Part 1 Upload (drop index keys + resolver), Part 2 Summarize onto child-records route with a STOP if the route refuses parentless documents or loses container/owner behaviour (then Part 1 only + report). NEXT: review its diff, merge `--no-ff` into work, Step 9.5 gates (code-review + adr-check + one adversarial pass), PR; deploy each wizard + live check only on owner's go (Matter→Document temporary profile needs a go too). History of the escalation: SummarizeFilesWizard does NOT use the shared pipeline. Shared `SummarizeFilesWizard/SummarizeAnalysisStep.tsx:103-155` POSTs `/api/v1/documents` `{name, containerId}` (containerId read client-side from the user's BU `sprk_containerid`, :74-82); the BFF handler `DataverseDocumentsEndpoints.CreateDocumentAsync` (:909) authorizes and resolves the owner (`ResolveOwnerAsync` :946) but applies no field mapping. Docs are unfiled (no parent), so there is no mapping source. Proposed: (A, recommended) 029 migrates Document Upload only; Summarize recorded as out of FR-16's mapping scope (no parent), owner already server-side; file the client-side container derivation as a follow-up if confirmed; or (B) move Summarize onto the child-records route (scope change). Owner question pending. Then: remove index keys + resolver use, update tests (`uploadOrchestrator.test.ts`, `__tests__/DocumentRecordService.payload.test.ts`), build, PR, deploy each wizard on owner's go, live check under MAT-000113 (Standard). Need a temporary Matter→Document profile for the live check (owner go). |
| **028 (done)** | **028** Analysis wizard + email mapping at send (FR-16, FR-17), FULL. Merged (#3133). Live check PASS on dev 2026-11-25 16:35–16:46Z (runs B, C; run A = correct Restricted refusal) + UAC-r2's To Do/Event pair-agreement check PASS. Done except the owner's dark-mode look. Follow-up #3148 (no BFF change). Detail: notes/task-028-notes.md. |
| **026** | Done except the owner's dark-mode look: on master (#3049, #3067), live on dev, live check + event re-check PASS (notes/task-026-notes.md). Dev rows: WAs 4dbaa0c7…, adff04f4… (+SPE container), 9891e505…, 29554738…; events a9b1aaee…, d5d8d35b…, 90587042…. |
| **Unpushed** | A local commit with the 028 notes (live-check results) and this file, held until #3148 merges so CI does not restart. Push after the merge. |
| **Next action** | Task 029 DONE 2026-11-25: #3158 merged (520ca7917d); upload, Summarize and Console are live on dev and both live checks passed (notes/task-029-notes.md); temporary profile d349f3a0… deactivated. Filed DI-042 #3156 and DI-043 #3176. (1) The 029 close-out docs are pushed to the work branch (046ad31f2d + master merge 8d556af9db). OPEN OWNER QUESTION: open a docs PR with merge-when-green? Access banner on MAT-000113 reported to UAC-r2 (theirs); it did not reproduce on reload at 21:35Z. (2) Once word-add-in-r1 #3157 is on master, add EmailAttachmentAddPlanner to the OfficeDocumentSaveGuardTests scanned list, with must-fire and must-not-fire controls. (3) Next: task 035 (delete the client mapping engine) on the owner's go. Remove C:	mpw029 with Remove-WorktreeSafe. |
| **027 history** | 027 live check on dev FAILED 2026-11-24 22:52Z: nothing mapped (the wizard names no `mappingSource`, and `sprk_reportcard` is not in `InferMappingSourceTables`, which lists created tables, not parents). Picks kept, owner = matter team. Dev rows: report cards 45edd549…, 737f7880…. Fix forward (owner): `reportCardService.ts` names the regarding parent as `mappingSource` like the other wizards. PR #3083 merged to master 2026-11-25 00:09Z as e70ff38894 (only the service + its payload test changed in the wizard's inputs since 3c1018c97f). Redeployed to dev 01:23Z (owner's go), read-back SHA matches. Re-check 2026-11-25 02:45Z PASS: picks kept, unpicked filled from the Matter, owner = matter team; dev rows db9d21bd…, 793e1b62…. 027 done except the owner's dark-mode look (like 023/024/026). Next: task 028 (Analysis wizard) on the owner's go. Detail: notes/task-027-notes.md. |

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
