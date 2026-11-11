# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.
> - **NOTE:** master is ahead of this work branch on project files: #2685 (090 housekeeping), #2697 (close 194) and #2698 (191 design and tasks) landed there. This copy is the session's state carrier. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-11 (checkpoint #124, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md`, `private-handoff-2026-11-08.md`, `private-p1-2026-11-11.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended. The owner said "Build in uac-r2 now" for **191** (D-13) and **193** (D-14). Round-90 rules apply. This session (`uac-r2`) is authoritative. Merges: run `git merge-tree`, then squash with `--match-head-commit`, after Router plus Client lint are green. Run ONE merge-watcher loop at a time. Live actions need the owner's go IN THIS SESSION. |
| **Next action** | (1) Review and merge the PRs from the 191b agent (`C:\wt191b`) and the 191d agent (`C:\wt191d`) when they report. (2) When one of them finishes: start 191f (Opus, fresh short-path worktree from origin/master; at most 2 agents at once besides 193). (3) Wait for the 193 agent's PR (#2700), then review and merge it. (4) Review fm's piece-3 diff when it arrives: the unset / valid / invalid settings conditions must resolve through the same `DirectMessageOwnerTeam` lookup as 198, and the refusal order must change only the code, never whether a write is refused. (5) After ebilling's 116 merge: the next dev deploy uses the same recipe (local merge of origin/master + spike `755987b385`; the ledger conflict resolves to 153 + master's delta). It must include ontology #2695 (`25ac8ea931`, merged after `ddae82fa90`), which ontology asked for. |

## Done today (2026-11-11)
- Merged: D-5 #2690 `67d0ef01d4`; D-3 #2691 `2552a69fc9`; 090 housekeeping #2685 `f563b8f689` (12 D-1 tasks + 186 closed, 178 deferred, disposition, test-diet, docs, lessons).
- Dev BFF = **`b3ca99cad5`** (LOCAL merge: origin/master `ddae82fa90` incl. ebilling #2689 `2fc434c912`, D-3, D-5 + spike `755987b385`; never pushed; worktree `C:\wtdep2`). Owner go "if a deploy is needed then its approved". Gates: build 0 warnings, ArchTests 2431/2431, 40.57 MB package, 4 files SHA-256 matched, `/healthz` 200, `/ping` 200, anonymous MCP POST 401. ebilling, mcp-r1 and ontology-r1 were notified. `C:\wtdep` was removed. ontology 177 PASS (on dc4f28ded8); 176 is waiting for a Revise-budget Work Item.
- Merged: #2697 `f5ee2a3065` (194 closed) and #2698 `ddae82fa90` (191 design + tasks).
- fm 054b and 054c (with the participant-Read fix and `-DirectShareJobLive`) were approved and merged into the fm work branch. K3 was filed as #2701. The first live Move runs only after the owner's go, scoped to one FMSWP- message.
- 194: the post-194b gate PASSED. The writes switch has been on since 2026-10-30, with the owner's go. 768/768 rows are in the right business unit; 0 failed. Closed in #2697 (merge pending). Filed #2696. Added U-13 (an owner moves a test matter between business units).
- Reviews done: ebilling #2689 (remerge reads `07ed87e2d4` and `a6f57558ef`), 116 rounds 2–6, and fm's 054b scan.

## Owner decisions (2026-11-11)
- D-1..D-12 "Accept all"; D-13 191 and D-14 193: "Build in uac-r2 now".
- **191 Q1–Q7:**
  - Q1 (a): field-lock the 3 columns.
  - **Q2 (b): an item-level edit permission plus a live test with the BFF identity (an owner go when due).**
  - Q3 (a): Read + AppendTo.
  - Q4 (a): Restricted stays closed. The owner's clarification was confirmed: Restricted is internal-only, and Secure is the way to share externally.
  - Q5 (a): the whole environment.
  - Q6 (a): invoices are SPA-only.
  - Q7: accept all four defaults.
- **P-1:** "Not yet". Leave the dev app user alone (private file).
- **fm 054b:** option A (the anchor writer is sanctioned). Refined in fm's session: no anchor when the thread is a strict pair OR any message has `sprk_directrecipient`; a fault means no anchor.

## Running agents / jobs
- **193 agent:** in `C:\wt193`, branch `fix/uac-193-add-person`. It is syncing about 3046 master commits, running gates and two adversarial passes, then opens a PR (not merged).
- **191b agent** (Opus, `C:\wt191b`, branch `task/uac-191b`) and **191d agent** (Opus, `C:\wt191d`, branch `task/uac-191d`): each opens a PR, does not merge, and makes no live writes.
- No merge watcher is running.

## 191 build order
- **Group A (parallel):** b (the `ExternalVisibility` predicate), d (the External Collaborators unit and role), f (field security on the 3 columns).
- **Group B:** c (the team share; after b and d; it rebases on 193/194), e (the placement job; after d), g (Office item permission; after b; the live test comes first), h (SPA/AI predicate use; after b).
- **Serial:** i (dev migration and live gate; 7 owner gos), then j (docs; the `.claude/constraints/auth.md` edit is made by the main session).

## Peers
- **ebilling-r1:** #2689 has auto-merge on, by the owner's "Merge on green CI". It sends the merge SHA, then I redeploy. 116 ships after #2689.
- **field-mapping-r1:** 054b pass 2 is in progress. Then I review the diff, checking:
  - the uac thread writers are unaffected;
  - the #2110/#2143 test changes, old against new;
  - the `sprk_directrecipient` limit is written into the notes and the runbook.

  054c comes after.
- **ontology-r1:** #2695 (the association-status fix, owner-chosen) takes the master merge after D-3. No review is needed.
- **mcp-r1:** deploy with a local merge of the spike tip `755987b385` until they say otherwise. `C:\wt174h` is not ours (PR #2580).
- **Names:** ebilling `ebilling-r1`, field-mapping `field-mapping-r1`, ontology `ontology-r1`, provisioning `provisioning-r1`, mcp `mcp-r1`, R4 `spa-r4`.

## Open items for the owner
- UAT U-1..U-13 (`notes/wrap-up-uat-and-decisions-2026-11-11.md` on master).
- D-2 steps 2–6 (fm 054b → Direct setup + settings (owner go HERE) → 054c Stamp → piece 3 → 054c Move). The `DirectMessage:*` settings stay unset until 054b verifies.
- D-10: the 142 setting, after U-3.
- 186 follow-up: export the Workforce role into SpaarkeMaster, then drop the package-scope exclusion.
- Quality alerts #1821 and #1906 are open; don't touch them unless asked.

## Scratch
- Worktrees: `C:\wtdep2` (the live dev deploy merge; keep it until the next deploy), `C:\wt191b`, `C:\wt191d`, `C:\wt090` (docs/uac-194-close), `C:\wt193`, `C:\wt191`, `C:\wtd3`, `C:\wtd5`, `C:\wt198`. Remove only with `Remove-WorktreeSafe.ps1` once their PRs merge.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone. The path exists on master, so a merge here needs care.
