# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-23, starting task 024. Desktop session `field-mapping-r1`.
> Previous version (055/056 build, deploy and live checks): `notes/handoff-history/current-task-2026-11-23-pre-024.md`.
> State only here; decisions in `notes/decisions.md`, standing rules in the project `CLAUDE.md`.

## Quick Recovery

| Field | Value |
|---|---|
| **Session / worktree** | `field-mapping-r1`, `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`, on `work/field-mapping-server-write-path-r1`. |
| **Work branch** | 98b2084536 (local; origin at 8b816e10b3). 055 and 056 are on master via #2984 (889b0ea94d). |
| **Dev** | BFF a48398746d (carries #2984). Web resources `sprk_communicationconversationpage.html` and `sprk_spaarkeai` from master 738648c7fb. `RecordRepair:Pass:GoLive` unset everywhere. |
| **Active task** | **024** To Do wizard migration (FR-16), FULL rigor, sonnet @ high, STEP MODE directional. Gate met: 023 live check PASS 2026-11-23 (dark-mode look left for the owner). |
| **024 approach** | Copy the 023 Event wizard migration: `CreateEventWizard/eventService.ts` + `__tests__/eventService.fieldMapping.test.ts`, notes `notes/task-023-notes.md`. Implementation delegated to one Sonnet implementer in a worktree on `task/fm-024` (branched from 98b2084536); this session reviews, runs gates, merges `--no-ff`. |
| **024 current step** | Code merged into the work branch at 869c6cf494 (task/fm-024 fca6adcf22). Review + gates PASS (jest 16/119, tsc 0, client-lint ok, ADR clean). Agent worktree to remove later: `C:code_filesspaarke.claudeworktreesgent-a451c05c09d18c1b9` (Remove-WorktreeSafe.ps1). |
| **Files modified (024)** | All committed (merge 869c6cf494). |
| **Next action** | Owner decision: open the master PR for the work branch (carries 024 + docs). Then `code-page-deploy` of `sprk_createtodowizard` on dev (owner go), then the AC5 live check in the owner Chrome (new tab group): create a To Do under a matter with a mapping profile, check mapped fields on first load, user value kept, owner = owning team, SmartTodo launch, widget refetch. Then mark 024 done and start 025. |

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
