# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-12 (checkpoint, desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> Previous version (054 and 022b detail): `notes/handoff-history/current-task-2026-11-12-pre-022c.md`.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 060, 070–075, 076c, 045a, 045b, 045c, 021, 022, 032b-1, 032b-1b, and all of **054** (#2733, merge d878e78a90, 2026-11-12). Nothing deployed.
> **Done on the work branch, not yet on master:** **022b** (merge 88ef973e52; UAC-r2 approved 2026-11-12).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | Desktop session **`field-mapping-r1`**, worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`. |
| **Work branch** | `work/field-mapping-server-write-path-r1` at 308ad2bcb9 + this checkpoint. It includes master a654da59c4; master now also has #2733 and later commits (origin/master 76b2930961). Merge master in before the next PR. |
| **Next action** | **Start 022c (#2738, DI-027), FULL, opus/high.** Apply 022b's predicate (a Restricted or unreadable source maps only into a row that is Restricted) in `RecordCreationService.Repair.cs` and `FieldMappingPushSourceGate`. Step 1: write `tasks/022c-*.poml` (task-create conventions; the KEEP test paths; the brief starts `git switch -c task/fm-022c origin/work/field-mapping-server-write-path-r1`). Step 2: implementer (opus) in an isolated worktree. Step 3: one Opus adversarial pass, then the diff to UAC-r2. Reuse 022b's `InheritedAccessPermission` answer and `AccessPermissionFloor`; do not fork the predicate. |
| **022b (closed)** | Notes: `notes/task-022b-notes.md`, including the floor property UAC-r2 asked to keep true (every way the floor can be wrong strips, never maps) and K1–K6. Gates at the merge: sln 0 warnings, ArchTests 2530, the 022b classes 348/348, publish +9,579 B (40.07 MB), no vulnerable packages. |
| **PR #2733** | MERGED (d878e78a90). UAC-r2 has been sent the SHA. The office-scope `rest` job hit its 20-minute limit under load (run 43286164495; a successful run takes about 15 minutes). The job is not a required check, so auto-merge went ahead while its re-run was still going. Check that re-run's result. |
| **Cleanup due** | Remove the merged agent worktrees with `scripts/worktree/Remove-WorktreeSafe.ps1`: `agent-a7bb5f277747a0758`, `agent-ad75f716d72643979`, `agent-a517215addda1d41e`, `agent-a79f7a37318159efe`, `agent-a5507ddb739d82d6e`, `agent-a568fceea5ca4ebb6`, `agent-a3d7557598f7a6283` (all under `C:\code_files\spaarke\.claude\worktrees\`). Snapshot branch `work/field-mapping-server-write-path-r1-054` can go after the owner's OK. |
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

- **022c:** next (above).
- **045d:** Q8 (invoice family re-derive); proposal in `notes/task-045c-notes.md`.
- **052b:** scheduled repair job, contact owner rule, chat contact create.
- **032b-2/032b-3:** on the current `work/ebilling-r1` tip; two adversarial passes; the diff goes to ebilling-r1.
- **021:** deploy by UAC-r2 with the owner's go after master. **076:** re-run check (c) after UAC-r2 deploys 076c.
- **Later:** 023–029, 035, 036, 050/051, 053, 061–063, 090.

## Open owner items

- The live steps in the deploy order above (054b, 054c Stamp/Move on dev).
- sync-mode -Apply on dev; deleting snapshot branches `…-049c`, `…-030b` and `…-054`.

## Coordination

- Peers: `uac-r2` (desktop, `local_54acf7b0-9732-48a1-aa4c-2d840ded57e5`), `provisioning-r1`, `ontology-r1`, word-add-in, ebilling-r1. Access-defect details go to uac-r2 by message only. UAC-r2 owns `DirectThreadAccessService.cs`, `DirectMessageParticipantShares.cs`, `SecureChildShareReconciliationJob.cs` and `SecureChildReconciler.cs`; we don't edit them. `SecureRootInheritance` had one approved read-only edit (022b); any further edit needs their agreement.
- Nightly quality alerts open on master: #1906 (client-tests), #2060 (full-unit-tests). The `Api.Office.` contract classes crash the test host when run together; exclude them from combined runs.
