# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–98) and `notes/decisions.md`. Task 185 decisions: `notes/185-invite-code-design.md` §11–§12 (I-1..I-13). Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-14 (checkpoint #32, after a session restart).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path; file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | Adversarial pass 2 of 2 on PR #1827 (task 185) at `c87ca1fcb`, full diff `67c80623f..c87ca1fcb`, in a scratch worktree `C:/wts-r1827c`. A leftover `C:/wts-r1827b` from the interrupted pass may exist; it isn't ours to delete blindly, so check it first. |
| **Next Action** | When pass 2 reports: fix any F, then a scoped re-check of the fix diff. Then confirm provisioning (session now `spaarke-wt-customer-provisioning-orchest-f4`) has nothing open on T240d. Then merge #1827: `git merge-tree` check, `gh pr merge --match-head-commit`, no `--delete-branch`, Router green. Deploy master to dev with `scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev` from a clean worktree, and coordinate with word-add-in, who last deployed `3084f0ca1`. Run the server-side live gate (no CIAM sign-in), close 185, and tell provisioning and SPA-r3, who are waiting to rebase. |

## Task 185 (PR #1827, branch `uac/185-invite-code`, worktree `C:/wt185`)
- **Slices:** A, B1, B2, C, D, E, F.
- **Fix rounds:** `5116f9251` (I-9 materializer), `e1f182a64` (sign-in race), `647a2ba67` (I-10), `314fa1b24` (I-11 503), `e4ecd31f1` (I-12 fragment), `955027c1a` (single backend), `cff0fca9b` and `c87ca1fcb` (comments).
- **Gates:** BFF targeted tests 5412 passed; ArchTests 979/979; SPA 1019/1019; publish +18 KB.
- **Dev schema:** `Set-ExternalInviteCodeHashSchema.ps1 -Apply` done, and VERIFY PASS (2026-10-13).
- **Reviews:** adversarial pass 1 gave 1 F (fixed) and 4 K. Provisioning's T240d review: F1 fixed, then the two-call oracle accepted as K (owner 98d); F3 and K1 fixed; F4 comment fixed.
- **Owner decisions:**
  - 98: SHA-256; granting sends the invite; system mail.
  - 98b: partner grants never auto-invite.
  - 98c: the inviter never sees ExistingAccount.
  - 98d: the two-call oracle is a known limit.
- **Release:** `sprk_externalinvitecodehash` must ship in a CI-built SpaarkeMaster release before T240d production. Provisioning owns that gate.
- **The live E11 sign-in** goes on the owner checklist.

## Other lanes
- **Ontology:** #1805 merged. #1824 (142) is CLEAN at `226003f36`; they merge once all checks pass. #1834 (143) is CLEAN at `4356397a2` (4 K items sent, incl. K4: D-137 Q26 put Spaarke Basic User on BU default teams, which is wide; flagged to the owner as an FYI). Task 183 (#1797) closes after ontology's 148 live gate shows a 412 → Conflict.
- **SPA-r3 (A9):** 046, 047 and 058 wait for #1827. UAC reviews their binder and WorkforcePrincipalResolver PRs. Issue #1830 (SPE sweep "Invalid hostname") was triaged as not a security gap: one stale pointer; a Warning-level fix plus a data fix, non-urgent.
- **word-add-in:** task 126 (#1815) changes `Set-ExternalFlagForB2BGuests.ps1` (`-CustomerTenantIds` / `-AllGuestsExternal`). UAC's "no -Apply in Model 1" rule stays until the owner restates it.
- **Task 186 (baseline access only):** pending SPA-r3's principal contract. The Workforce role is excluded from SpaarkeMaster 1.2.2.0 until 186 makes it final; tell provisioning when it does.
- **Issues filed this period:** #1813, #1817 (UAC slow test) and #1830 (triaged); ontology filed #1818 and #1825.

## Open owner items
- Restate the Model 1 `-Apply` rule once #1815 merges.
- The live E11 sign-in (checklist §11).
- Task 186 ceiling decision.
- #1747 and #1776.
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.

## Not started
178, 136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping.
