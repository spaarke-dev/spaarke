# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-13 (checkpoint, desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> Previous version (054 and 022b detail): `notes/handoff-history/current-task-2026-11-12-pre-022c.md`.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 060, 070–075, 076c, 045a, 045b, 045c, 021, 022, 032b-1, 032b-1b, and all of **054** (#2733, merge d878e78a90). Nothing of this project deployed to dev except what the live dev BFF 4e64670ea0 carries (040 yes, 054 no).
> **On master via #2768 (merge d8b53ec0c3, 2026-11-13; #2738 closed):** 022b (88ef973e52), 022c (74556c46df). SpaarkeMaster 1.3.1.0 (published from d8b53ec0c3) carries the 054 source; its release row gained the 054b post-import steps (work branch, rides the next PR).
> **On the work branch, not on master:** 052b (b18d4fc394, closed 2026-11-13), 045d (0af492ed1c, merged 2026-11-13; close pending a fixture fix). Sync-mode applied on dev (2026-11-12); 054b applied on dev (2026-11-13).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | Desktop session **`field-mapping-r1`**, worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`. |
| **Work branch** | `work/field-mapping-server-write-path-r1` at c8d5f5bced + checkpoints: 052b merged (b18d4fc394), then master d8b53ec0c3 (c8d5f5bced). Gates on c8d5f5bced: sln 0 warnings, ArchTests 2539, project suites 8653/0/7, publish +18,893 B vs fresh master (40.13 MB, 191 = 191), no vulnerable packages. Not pushed to master; next PR carries 052b, the 1.3.1.0 release-row edit, and (when merged) 045d. |
| **Next action** | (1) **045d MERGED** into the work branch at 0af492ed1c (task/fm-045d 9e772af961; UAC-r2 re-PASS and ebilling-r1 PASS of fix round 5). Gates on 0af492ed1c: sln 0 warnings; ArchTests 2539; publish +35,546 B vs fresh master d8b53ec0c3 (40.15 MB, 191 = 191 files, Compress-Archive); no vulnerable packages. **Project suites: 8838 passed, 4 FAILED**: `SearchIndexDefaultTests` on `sprk_invoice` (4 tests). Cause: since 045d, an invoice create derives at step 5½ and reads the record-type catalog, which that test's strict mock does not answer ("record-type row for sprk_matter could not be read"). It is a fixture gap, not a product defect (the dev catalog has the rows). The fix is a separate fixture commit; then cherry-pick it onto the work branch and rerun verify-suites before closing 045d (POML status, TASK-INDEX row 045, current-task). (2) **045e (agent ab4c…, branch `task/fm-045e` tip 33d85f700d, worktree `agent-ab4c8673aed741696`)**: built (267 new tests, 176 fail with the rule off; new code `invoice_family_regarding_pair_core_record_mismatch`). The agent was asked for: a FULL ArchTests run (it reported 183, filtered); why the child PATCH applies the check "invoice only"; whether an id-only write can store a disagreeing id (K2). An Opus adversarial review of 0af492ed1c..33d85f700d is RUNNING. Then: fixes → diff to ebilling-r1 + UAC-r2 → merge → verify-suites → close. 045e found that the push never writes a pair (`CreateTimeFieldMapping.ApplyRule` skips pair rules), which contradicts `PushMayWrite` and the FR-23 comments (pre-existing, its K8): decide whether to file it. (3) **054c Stamp**: waiting on UAC-r2 step 3. (4) The next PR to master carries 052b, 045d, (045e), and the 1.3.1.0 release-row edit; it needs the owner's go. |
| **022b (closed)** | Notes: `notes/task-022b-notes.md`, including the floor property UAC-r2 asked to keep true (every way the floor can be wrong strips, never maps) and K1–K6. Gates at the merge: sln 0 warnings, ArchTests 2530, the 022b classes 348/348, publish +9,579 B (40.07 MB), no vulnerable packages. |
| **PR #2733** | MERGED (d878e78a90). UAC-r2 has been sent the SHA. The office-scope `rest` job hit its 20-minute limit under load (run 43286164495; a successful run takes about 15 minutes). The job is not a required check, so auto-merge went ahead while its re-run was still going. Check that re-run's result. |
| **Cleanup due** | Five merged agent worktrees were removed on 2026-11-12. Two remain, locked by this session's Claude process: `agent-a517215addda1d41e` and `agent-a3d7557598f7a6283`, both under `C:\code_files\spaarke\.claude\worktrees\`. They are merged and clean. Remove them with `Remove-WorktreeSafe.ps1` once the lock is released. Snapshot branch `work/field-mapping-server-write-path-r1-054` can go after the owner's OK. |
| **Drift check** | `scripts/check-task-status-drift.ps1 -Project field-mapping-server-write-path-r1`: statuses agree. Set mismatches remaining, all from before this checkpoint: 033 (dropped POML, no row), 049b (row, no POML), 022c (POML is next). |

## Binding deploy order (UAC-r2 D-2 revised, owner "Accept all" 2026-11-11; each live step needs the owner's go)

1. Dev BFF from master (after ebilling's batch; coordinate with mcp-r1). The `DirectMessage:*` settings stay unset, so piece 3 is off.
2. 054b: `Set-ThreadFlagFieldSecurity.ps1` dry run → `-Apply` → `-Verify` (owner go in THIS session).
3. Direct setup + the two `DirectMessage:*` settings (owner go in uac-r2's session), only after 054b `-Verify` exits 0 there. **Binding:** the settings stay unset on an environment until then. DI-026 (#2701) check before the settings go live.
4. 054c Stamp (dry run → owner go → `-Apply` → `-Verify`).
5. Piece 3 takes effect once the settings are set.
6. 054c Move (`-DirectShareJobLive`; first run scoped to one `FMSWP-` message, L16).

After every SpaarkeMaster import on an environment where 054b is applied: `Set-ThreadFlagFieldSecurity.ps1 -Verify`, and `-Apply` if it fails. **Assign invariant:** a message moves to the Direct team only with a valid stamped pair. Details: `notes/task-054-notes.md`.

## Known limits carried

- **022:** K2 (the isolation owner is the single Secure team) and K3 (the Project wizard's follow-on Event/WA creates map in the browser; tasks 023/024). K4 is DI-024 #2648.
- **022b:** K1–K6 in `notes/task-022b-notes.md`. Repair and push are 022c.
- **054:** 054a L3, L6–L10; 054b K1–K5; 054c L11–L16; piece 3 limits; #2728 (document linked only via `sprk_relatedcommunication`, UAC-r2 follow-up); #2722 (UAC-r2 builds it in `NonDirectUnfileAnswer`).

## Other lanes

- **022c:** closed (merge 74556c46df).
- **045d:** Q8 (invoice family re-derive); proposal in `notes/task-045c-notes.md`.
- **052b:** scheduled repair job, contact owner rule, chat contact create.
- **032b-2/032b-3:** on the current `work/ebilling-r1` tip; two adversarial passes; the diff goes to ebilling-r1.
- **021:** deploy by UAC-r2 with the owner's go after master. **076:** re-run check (c) after UAC-r2 deploys 076c.
- **Later:** 023–029, 035, 036, 050/051, 053, 061–063, 090.

## Open owner items

- The live steps in the deploy order above (054b, 054c Stamp/Move on dev).
- deleting snapshot branches `…-049c`, `…-030b` and `…-054`.

## Coordination

- Peers: `uac-r2` (desktop, `local_54acf7b0-9732-48a1-aa4c-2d840ded57e5`), `provisioning-r1`, `ontology-r1`, word-add-in, ebilling-r1. Access-defect details go to uac-r2 by message only. UAC-r2 owns `DirectThreadAccessService.cs`, `DirectMessageParticipantShares.cs`, `SecureChildShareReconciliationJob.cs` and `SecureChildReconciler.cs`; we don't edit them. `SecureRootInheritance` had one approved read-only edit (022b); any further edit needs their agreement.
- Nightly quality alerts open on master: #1906 (client-tests), #2060 (full-unit-tests). The `Api.Office.` contract classes crash the test host when run together; exclude them from combined runs.
