# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-09 (by context-handoff — session moving from the VS Code window to the Claude Code desktop session `field-mapping-r1`). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030, 034, 037–040, 042c, 043, 044, 046, 046b, 046c (repo), 048, 049, 049b, 049c, 052a, 070–075. Latest: #2629 (57748ebf6d).
> **W3 direction:** named exceptions plus guards, no core redesign in R1.

## Quick Recovery

| Field | Value |
|---|---|
| **Session** | This project now runs in the desktop session **`field-mapping-r1`** (same worktree `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`). Peers were told the new name. |
| **Work branch** | `work/field-mapping-server-write-path-r1`, pushed. Merged since #2629, not yet on master: 076c, 060, 045a, 021, 045c, 032b-1, 032b-1b (+ C1), 045b, guard-1 fix (8988c2499e), pair-type bind spelling fix + guard (8e0b48eadb), docs, POMLs 054 (with the 2026-11-09 option A and thread-lookup additions) and 022b. Builds 0 warnings. |
| **Active task** | **022 MERGED** into the work branch (10655f4dfe, `task/fm-022-6` at 152ba139c9, UAC-r2 OK). The merged branch: builds have 0 warnings and the filtered BFF run had 4917 passed and 0 failed. Round 6 fixed UAC-r2's findings: in fb10f30031 a pushed clear of a filing column from a secure source is always a filing change; in 546e557e93 an environment with no Secure Record business unit is decided by `sprk_issecure` alone, while one that is set up but whose team is missing, ambiguous or faulted is undetermined (UAC-r2 confirmed the `ResolveSecureOwnerTeamForCopyGateAsync` wrapper in their file; their 198 branch also edits `SecureChildShareSynchronizer.cs`, so whoever merges second runs merge-tree and rebuilds). K4 is filed as DI-024 / #2648. **History before the merge:** waiting on UAC-r2's OK. Branch to merge: **`task/fm-022-5`, head 65094251c2** (local; = `task/fm-022-4` 85c70c4431 + 3 commits). After 85c70c4431, a full Opus `adversarial-reviewer` pass over the whole 022 diff (2026-11-09, desktop session; this replaces the planned scoped Sonnet re-check) found no secure-value leak and no fail-open. Its F1 (the preview asked the gate with lookup binds the apply would refuse on reparentable children) is fixed in 6f6a62ab99: one helper, `FieldMappingPushLookups.ConfineToNamedColumn`, called by both. F4 (docs) is fixed in 30a66762ce. Notes: 65094251c2 (headed "Fix round 5 (review F1, F4)"). The fix diff was checked in the main session. Fail-then-pass 1/29 → 30/30; filtered BFF 4394/0; 0 warnings; no vulnerable packages. Known limits K1–K4 are in the notes. |
| **Next action** | 1. **054a piece 2, code:** the column is LIVE on spaarkedev1 (owner go 2026-11-09; `scripts/Set-DirectRecipientSchema.ps1`, -Verify passes; navigation `sprk_DirectRecipient`, read `_sprk_directrecipient_value`; sent to UAC-r2). Next is the BFF stamp at create on Direct messages (send, respond, inbound join, new Direct thread), on a `task/fm-054a-3` branch. The piece-3 contract is in `notes/task-054-notes.md`. 2. Start 022b. 3. Prepare the next PR to master (row below). 4. Clean up the agent worktrees `.claude/worktrees/agent-a60710f842ad3a380`, `agent-a892fa0f61f363b8a` and `agent-a7bc7a1ae62bf9182` with `Remove-WorktreeSafe.ps1`. |
| **Next PR to master** | The bundle above + 022. Before it: `scripts/verify-suites.sh`; `scripts/publish-delta.ps1` against a fresh master; send UAC-r2 the combined diff; open the PR from a snapshot branch `work/field-mapping-server-write-path-r1-<tag>` with auto-merge. PR text: ADR-008 path A; 032b-1's push side effect (invoice display copies never pushed); Office Assigned To targeting; 022's create strip + push gate; the one-line ontology-owned change in `DecisionActionExecutors.cs` (ontology reviewed it: OK). |

## 022 known limits carried (from the 2026-11-09 Opus pass; the detail is in the notes)

- **K1:** a missing Secure Record owner team reads as "undetermined", so team-owned sources map or push nothing. A distinct code is proposed.
- **K2:** the isolation owner is the single Secure team.
- **K3:** the Project wizard's follow-on Event and Work Assignment creates still map in the browser (tasks 023/024 own them). The wizard's lock reads an empty secure flag as "not secure"; the server still refuses.
- **K4:** an adapter default is not re-applied after the strip (unverified).

## Other lanes

- **054:**
  - **054a piece 1** (lookup + settings) is FINAL on `task/fm-054a-2` (code ac6f1743b3); UAC-r2 merged it into 198. The consumer rule (`IsIdentified` first) is owned by 198.
  - **Rest of 054a** waits for UAC-r2's 198 Part C. It now includes `sprk_directrecipient` (owner option A, 2026-11-09): stamped at create on Direct messages; inbound that can't be mapped → no recipient + a stable Error logged with the message id (198 treats "no recipient" as terminal; a later stamp must bump `modifiedon`).
  - **054b** secures the thread flags, `sprk_sentby`, `sprk_directrecipient` and `sprk_communicationthread`. Its dry-run evidence covers every thread-move path, including ThreadResolver (app-only). The fallback (re-grant only when the thread is unchanged since filing) touches UAC-r2's `AfterRefileAsync`: agree it with UAC-r2 first.
  - **054c** moves rows and backfills the recipient (exactly 2 distinct enabled users, one = `sprk_sentby`, else list and skip).
  - 054b and 054c live runs need the owner's go.
  - Send UAC-r2 the confirmed Web API navigation property name of `sprk_directrecipient` once it exists.
- **022b:** starts after 022 merges (Restricted mapping source; document access permission set in the create). UAC-r2 reviews.
- **045d:** Q8 (invoice family re-derive); proposal in `notes/task-045c-notes.md`.
- **052b:** scheduled repair job, contact owner rule, chat contact create.
- **032b-2/032b-3:** on the current `work/ebilling-r1` tip; 032b-2 gets two adversarial passes; the diff goes to ebilling-r1.
- **021:** deploy by UAC-r2 with the owner's go after master. **076:** re-run check (c) after UAC-r2 deploys 076c.
- **Later:** 023–029, 035, 036, 050/051, 053, 061–063, 090.

## Done this session (not project work)

- `projects/messages-enhancements-r1/` (requirements capture) merged to master via #2646; Idea #2645 on Project #2. Its branch's worktree is at `C:\wtmsg` (sparse). Remove it only with `scripts/worktree/Remove-WorktreeSafe.ps1`. The main checkout `C:\code_files\spaarke` was NOT fast-forwarded, because it has another session's local changes.

## Open owner items

- sync-mode -Apply on dev; deleting snapshot branches `…-049c` and `…-030b`; 054b and 054c live runs.

## Coordination

- Peers: `uac-r2` (desktop), `provisioning-r1`, `ontology-r1` (desktop, takes over from `spaarke-wt-spaarke-ontology-platform-r1-38`), word-add-in, ebilling-r1. Access-defect details go to uac-r2 by message only.
- UAC-r2 task 198 is the share side of 054 and uses the audience pair {`sprk_sentby`, `sprk_directrecipient`}.
