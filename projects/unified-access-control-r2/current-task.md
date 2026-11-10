# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-10 (checkpoint #108, desktop session `uac-r2`). Private details: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md` (outside the repo; read it first, never copy it into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. This session (`uac-r2`, Claude desktop) is authoritative. Merge: `git merge-tree`, then squash with `--match-head-commit`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. Bash `grep`/`sed` loops HANG: use Grep/Read/PowerShell. |
| **Active task** | **198**, the share side of fm 054 (Direct 1:1 messages), FULL. Branch `task/uac-198-direct-share-side` in `C:\wt198`, tip **`a0ef30a6d6`** (pushed). Notes `notes/task-198-direct-share-side.md` §1–§7.6 (on the task branch). |
| **198 done** | Parts A–E; adversarial pass 1 + fixes; R2 (participants from the stamped pair); R3 (Restricted bar durable); Part E (D-215 gate Decision Records, ontology rulings R-a/R-b/R-c accepted); adversarial pass 2 → fix round (F1 content-row owner confirm, F2 carry cap/cursor, F3 GrantAndConfirm, R-b `finally`, ontology N1–N4, item 7 Read-bit POA filter, projection pins). Gates at `a0ef30a6d6`: scoped 2964, +MembershipReconcile 2978, GateDR+Signals+Writer 688, ArchTests 2119, BFF 0 warnings. `git merge-tree` vs origin/master: clean. |
| **198 running** | (1) uac scoped re-check (Sonnet) of F1/F2/item 7/pins. (2) ontology-r1 scoped re-check of its part (F3, `finally`, N1–N4). **Hold merge until both report.** |
| **Next action** | On both re-checks clean: publish-size delta vs a FRESH master build; CVE check; PR body (ontology's R-a §11 lines; K-E-4 named as a known limit); merge into the project branch. Then put the deploy sequence to the owner (below). |
| **Deploy order (binding, agreed with fm)** | fm 054a piece-2 stamp → fm 054b field security (incl. three run-as-user writers) → fm 054c recipient backfill (dry run, owner go, apply) → 198 deploy. Part A Direct role setup before fm 054a piece 3. Each live step needs the owner's go in its own session. |
| **Dev state** | BFF = 6fbb3a7642 since 2026-11-08 (verify before any deploy). |

## Owner answers (2026-11-09/10)
- ebilling 130c: with "visible to firms" off the rule set name is hidden (EB-D-209); rejections carry reason code + firm-safe description, a written reason overrides (option C, in this project; EB-D-210).
- Budget inquiry email (ontology D-216): filed under the inquiry AND its matter; **internal-only** (2026-11-10).
- Earlier: Direct 1:1 = a separate owner team; keep access after filing; #2270/#2278 = a customer-BU column.

## Reviews state
- **ebilling 130-0:** diff 1 (3b–3f) APPROVED; diff 2 (3g) APPROVED; diff 3 = F3b (to come). One dev batch with 130-0b/130b/F3b; nothing to dev without the owner's go here.
- **ebilling 130d** (firm-safe reason table): APPROVED pending their pass 2 (nits: core-record wording incl. work assignment; control/bidi filter at read). DEF-060 writer (Option 3 per-rule description vs per-rejection text): owner deciding in ebilling's session.
- **ebilling 166:** field security applied on dev (their owner go); read check PASS; new-BU coverage filed as DEF-066 #2651.
- **ontology D-216 / task 176:** the resolver release for `sprk_regardingservicerequest` (matched-matter only, fails closed) comes to me as its own SHA, then the full range.
- **fm:** 054a piece 2 in their review; outbound Direct create routing (resolver-create gap) is their owner's decision; I review it.

## Open owner items
- Private: one dev identity-hygiene question (in the handoff).
- To report: the 130-0 F1-2 trade-off (known limit).
- #2270/#2278 design (customer-BU column).

## Filed this stretch
#2647 (record-anchored grant has no durable retry), #2648 (fm K4), #2651 (ebilling DEF-066).

## My work queue after 198
1. #2270/#2278 design. 2. Backlog: #2627, #2621, #2585, #2576, #2524, #2543; parked 193/197/196a/196b; TASK-INDEX reconcile at 090.

## Peers (cross-session names)
ebilling `spaarke-wt-ebilling-r1-c7` · field-mapping `field-mapping-r1` · ontology `ontology-r1` · provisioning `provisioning-r1` · word-add-in `word-add-in-r1` · mcp `mcp-r1`.

## Private items (never in issues/PRs/commits)
See the private handoff. Identifiers are fine in public; "X can still read/write Y today" for an open gap, or an identity's role holdings, is not.
