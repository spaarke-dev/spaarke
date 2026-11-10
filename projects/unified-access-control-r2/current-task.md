# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-10 (checkpoint #109, desktop session `uac-r2`). Private details: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md` (outside the repo; read it first, never copy it into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. This session (`uac-r2`, Claude desktop) is authoritative. Merge: `git merge-tree`, then squash with `--match-head-commit`. Live actions, deploys, and role or settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. Bash `grep`/`sed` loops HANG: use Grep/Read/PowerShell. |
| **Active task** | **198**, the share side of fm 054 (Direct 1:1 messages), FULL. **REVIEW-COMPLETE.** Branch `task/uac-198-direct-share-side` in `C:\wt198`, tip **`28208ed81b`** (pushed). Notes `notes/task-198-direct-share-side.md` §1–§7.6 are on the task branch; §7.7 for the final fix round is still to write (see Next action). |
| **198 final state** | Parts A–E; adversarial passes 1 and 2 with their fix rounds; my scoped re-check (no F; K1/K2 fixed); ontology's re-check (F2 tests `127f76bd54` accepted). Final round: `ab43dee237`/`1975c5a45a` tests; FIX A `d1116b0ef5` (attempted-set undo in `finally`); FIX B `ad96240617` (non-Read share holders skipped, never revoked); FIX C `4c3a7c5336` (gate record's live owner read before granting; compared against the creating team; ontology PASSED it); FIX D `c06ae85c86` (comment); message-row confirm-and-undo `b844296a6c`/`28208ed81b`. Gates at the tip: filtered 5489/0, ArchTests 2119/0, BFF 0 warnings. Publish delta vs fresh master `a0e1b4ab7c`: 39.26 → 39.33 MB (+0.07), 191/191 files, `Compress-Archive`. CVE: none. merge-tree vs master and vs fm's work branch: clean. |
| **Merge path (decided)** | 198 carries fm's unlanded work as merges (fm-021, 032b-1/1b, 045a/c, 060, 076c, 054a lookup). Diff vs master is 135 files; vs `origin/work/field-mapping-server-write-path-r1` it is 51. **Open the 198 PR to master only after fm's work branch is on master**, so it carries 198 alone; that matches the deploy order. I asked fm when their branch reaches master. |
| **Next action** | (1) On fm's answer: when their branch lands, merge master into 198, rebuild, re-run gates, and open the PR. The PR body carries the placement justification, the +0.07 MB delta, no CVE, ontology's R-a §11 lines, FIX C's creating-team comparison, and known limits K-E-4, K-F3, K-N3, K-N2, persistent carry overflow (K3) and the unreadable-share thread never reconciling (K4). Send ontology the merge SHA. (2) Write notes §7.7 on the task branch (final round + Ks). (3) Then put the deploy sequence to the owner. |
| **Deploy order (binding, agreed with fm)** | fm 054a piece-2 stamp → fm 054b field security (incl. three run-as-user writers) → fm 054c recipient backfill (dry run, owner go, apply) → 198 deploy. Part A Direct role setup goes before fm 054a piece 3. Each live step needs the owner's go in its own session. |
| **Dev state** | BFF = 6fbb3a7642 since 2026-11-08 (verify before any deploy). ebilling DEF-061 column added on dev 2026-11-10 (their owner go; no access columns). |
| **Scratch** | `C:\wt198m` = fresh-master worktree for the size check (remove with `scripts/worktree/Remove-WorktreeSafe.ps1`); `C:\p198m`, `C:\p198b` publish folders and `C:\tmp\p198*.zip` can be deleted. Untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree was left by an agent; leave it alone. |

## Owner answers (2026-11-09/10)
- ebilling 130c: with "visible to firms" off, the rule set name is hidden (EB-D-209). Rejections carry a reason code plus a firm-safe description, and a written reason overrides it (option C, in this project; EB-D-210).
- Budget inquiry email (ontology D-216): filed under the inquiry AND its matter; **internal-only** (2026-11-10).
- Earlier: Direct 1:1 = a separate owner team; keep access after filing; #2270/#2278 = a customer-BU column.

## Reviews state
- **ebilling 130d:** FINAL and APPROVED at `69a8b2db72`. Firm reason text: rune walk, dropping Control/Format/LineSeparator/ParagraphSeparator characters on both paths; a Duplicate never takes the override; the DEF-060 writer must use the same predicate. Ships in one dev batch with 130-0b/130b/F3b; nothing goes to dev without the owner's go here. 130-0 diff 3 (F3b) is still to come.
- **ontology 176 (D-216):** resolver release APPROVED at `e8dcb37fa9` + guard `73b7bb7787` (option A: on a succeeded read a missing key = null except the matter, which must be present and equal; pair = M with matter type; guard requires `IsSecured=0` on the 8 columns). Send/reply `045856ad10` approved. Round 2 is running on their branch: F4 test, F1 archive warning, F2 tests, the service request stays primary, provenance, and **K7 = the inquiry thread is created Private** via `ThreadResolutionRequest.CreatePrivate` (P1; conditions: server-set only with `[JsonIgnore]` plus a bind test, create-only, a join leaves an Open thread Open). Ks accepted: K5 (only Outbound counts as an inquiry), K8 (add-in path, DI-147), K9 (no backfill). Re-check their round-2 SHA range when it arrives.
- **fm 054a piece 2:** APPROVED + C1 test `d6dce26626` accepted; merged into fm's work branch. Follow-up #2652 (converge onto my reader after 198).
- **fm outbound Direct create routing:** the owner's decision in fm's session; I review it.

## Open owner items
- Private: one dev identity-hygiene question (in the handoff).
- To report: the 130-0 F1-2 trade-off (known limit).
- #2270/#2278 design (customer-BU column).

## Filed this stretch
#2647, #2648, #2651, #2652 (fm, DI-025).

## My work queue after 198
1. #2270/#2278 design. 2. Backlog: #2627, #2621, #2585, #2576, #2524, #2543; parked 193/197/196a/196b; TASK-INDEX reconcile at 090.

## Peers (cross-session names)
ebilling `spaarke-wt-ebilling-r1-c7` · field-mapping `field-mapping-r1` (`local_b58f031d…`) · ontology `ontology-r1` (`local_72ca5a70…`) · provisioning `provisioning-r1` · word-add-in `word-add-in-r1` · mcp `mcp-r1`.

## Private items (never in issues/PRs/commits)
See the private handoff. Identifiers are fine in public; "X can still read/write Y today" for an open gap, or an identity's role holdings, is not.
