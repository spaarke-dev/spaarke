# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-25, task 028 built and gated; PR next. Desktop session `field-mapping-r1`.
> Previous version (055/056 build, deploy and live checks): `notes/handoff-history/current-task-2026-11-23-pre-024.md`.
> State only here; decisions in `notes/decisions.md`, standing rules in the project `CLAUDE.md`.

## Quick Recovery

| Field | Value |
|---|---|
| **Session / worktree** | `field-mapping-r1`, `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`, on `work/field-mapping-server-write-path-r1`. |
| **Work branch** | Pushed. Master has #3049 (dc33f14d35), #3067 (68c2a4c968), #3072 (3c1018c97f) and #3083 (e70ff38894). |
| **Dev** | BFF 996928732e (UAC-r2). `sprk_createworkassignmentwizard` from master 68c2a4c968 (17:46Z, SHA CF8B07B1…FF603702). `sprk_createtodowizard` from 2566d561e0. `sprk_createreportcardwizard` from master e70ff38894 (2026-11-25 01:23Z, SHA 728C5DC4…8B7BC999; rollback = the 3c1018c97f bundle 3F0701D3…). `RecordRepair:Pass:GoLive` unset everywhere. |
| **Active task** | **028** Analysis wizard + email mapping at send (FR-16, FR-17), FULL. On the work branch: d1fef67b64 (agent), fa92efe766 (email step allows an empty subject/body, owner 2026-11-25), b9e426ff78 (review fixes: analysis narrowing on the send fill, mapped-length cap, tests). One adversarial review done. Gates green (the load timeouts rerun alone: 398/398); publish +4,160 bytes (41.56 MB), no CVE. DI-040 / #3126 filed (typed over-long subject). Detail: notes/task-028-notes.md. |
| **Previous task** | **027** Report Card wizard (FR-16, FR-02 regression), FULL, sonnet @ high, directional. Implementer agent (isolation worktree, branch `task/fm-027` from 9f61f10961) is making the change; this session then reviews, runs gates, merges `--no-ff`; then PR, deploy `sprk_createreportcardwizard`, live check — each on the owner's go. |
| **027 inventory** | `reportCardService.ts`: `applyFieldMappings` import :50, call :242 (inside the association block); create via `this._dataService.createRecord` :259 (already the BFF route through `withBffChildWrites` :154, no warnings surfaced). Plan: `createRecordViaBffWithWarnings`, no `mappingSource` (the regarding Matter/Project is the source; route infers it); full-column lookup hints. Dev: report card lookups sprk_AssignedAttorney1/2, sprk_AssignedParalegal1/2, sprk_AssignedToLawFirm1 + sprk_AssignedLawFirm2 (→ sprk_organization), sprk_AssignedtoExternal, sprk_AssignedToInternal. Profile "Matter to Report Card (Attorney Matrix)" b2915dad-587b-f111-ab0e-7ced8ddc4a05 active, 8 Manual Refresh rules incl. lawfirm1 → sprk_assignedtolawfirm1, external, internal. Pickers in CreateReportCardStep.tsx forward ids; no change needed. |
| **026** | Done except the owner's dark-mode look: on master (#3049, #3067), live on dev, live check + event re-check PASS (notes/task-026-notes.md). Dev rows: WAs 4dbaa0c7…, adff04f4… (+SPE container), 9891e505…, 29554738…; events a9b1aaee…, d5d8d35b…, 90587042…. |
| **Uncommitted** | None. |
| **Next action** | 028: PR #3133 open; owner said merge when green (2026-11-25) → GitHub auto-merge set (merge commit) after the Client lint fix 2034bc91e2 (4 redundant checks after the new fetch guard; 2 suppression counts pruned by hand — a full `prune` locally rewrites unrelated entries from stale installs, never commit that). The api-office office-scope leg was cancelled at the 20-min cap (not required; Tier 2 api-office passed). After the merge: sync the main checkout (ff only). Then on the owner's go: the BFF deploy through UAC-r2's candidate at the exact SHA; the code-page deploys `sprk_spaarkeai` (widget host) and the Playbook Library page; two temporary dev profiles (`* -> Analysis`, `Analysis -> Communication`); the live check (mapped fields on first load, user value kept, owner = team; email with an empty subject filled, typed subject kept). word-add-in-r1 138 (#2209) got our guard OK. |
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
