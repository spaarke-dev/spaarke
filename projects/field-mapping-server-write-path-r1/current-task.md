# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-12 (checkpoint, desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> Previous version (054 and 022b detail): `notes/handoff-history/current-task-2026-11-12-pre-022c.md`.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 060, 070–075, 076c, 045a, 045b, 045c, 021, 022, 032b-1, 032b-1b, and all of **054** (#2733, merge d878e78a90). Nothing of this project deployed to dev except what the live dev BFF 4e64670ea0 carries (040 yes, 054 no).
> **On the work branch, in PR #2768 (open, UAC-r2 PASS, auto-merge ON per the owner):** 022b (88ef973e52), 022c (74556c46df).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | Desktop session **`field-mapping-r1`**, worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`. |
| **Work branch** | `work/field-mapping-server-write-path-r1` at f6025e4212 + this checkpoint; includes master e9208abc5e (merge c1afee6e02). PR #2768 head = snapshot `work/field-mapping-server-write-path-r1-022c` at d7f39d2a0d (verify-suites 8553/0/7, Arch 2536, publish +13,136 B vs e9208abc5e, no vulnerable packages, bound to this session; Auto-fix offered, not answered). |
| **Next action** | (0) **054b on dev: DONE 2026-11-13** (dry run, -Apply, -Verify all exit 0; reported to UAC-r2; notes/task-054-notes.md "Live record"). DI-026 dev check PASS (read-only, 2026-11-13). Waiting for UAC-r2 step 3 (their gates: 30-min BFF gate, spa-r4 E2E, their owner's go), then 054c Stamp here. (1) **045d (implementer agent ad45…, branch `task/fm-045d` tip 1a979b256b, agent worktree `agent-ad459522625442188`)**: RUNNING. The owner chose (b) on the K4 blanks: type, id and URL are always written, name and number when present; a missing record or read fault still refuses; submission writers unchanged (decisions.md 2026-11-12). The agent builds (b), then the app-only half (UpdateRecordActionCore, DataverseUpdateHandler, push; existing refusal forms, no new codes), fixes the POML reason, reverifies. Then: one Opus adversarial pass → diff to UAC-r2 + ebilling-r1 → merge `--no-ff` → publish delta → close. (2) **052b (agent abee…, branch `task/fm-052b`, worktree `agent-abee2a05100f8d4fc`)**: the Opus pass of 1e72d50962 is DONE (F1 Assign cascade, F1 secure-parent order, F2 fault retry, F2 unconfigured team, Ks K5–K7, K9, K10). UAC-r2 confirmed the resolver change with 4 conditions. The fix round, with all of these, is RUNNING (relayed 2026-11-12; UAC-r2 told). DI-028 #2771 filed. Then: Sonnet re-check of the fix diff → diff to UAC-r2 (list RecordOwnershipResolver) → merge `--no-ff` → publish delta → close. (3) **PR #2768**: auto-merge ON (merge commit, owner "Merge when green"). On merge: send UAC-r2 the SHA, close #2738, and update TASK-INDEX (022b/022c "on master"). (4) **Sync-mode on dev**: APPLIED 2026-11-12 (36 changed, 0 failed, VERIFY PASS). (5) **054 live steps** (owner go given 2026-11-12): UAC-r2 deploys dev BFF e9208abc5e+spike on THEIR owner's go and sends the live SHA; then here 054b `Set-ThreadFlagFieldSecurity.ps1` dry run → `-Apply` → `-Verify` (report to UAC-r2); then UAC-r2 step 3; then 054c Stamp. |
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
