# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–99) and `notes/decisions.md`. Task 185 decisions: `notes/185-invite-code-design.md` §11–§12. Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-14 (checkpoint #33).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path (security counts); file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | The task 187 implementer (Opus, xhigh) in worktree `C:/wt187`, branch `uac/187-external-live-parent` (from master `6da771b96`). It opens a PR; it does not merge or deploy. |
| **Next Action** | When 187's PR is up: **two** adversarial passes (security), then fix rounds until no F remains, a scoped re-check of each fix, merge (`git merge-tree` check, Router green, `--match-head-commit`, no `--delete-branch`), deploy master to dev (`scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev`, from a clean worktree; tell word-add-in, since the owner is UATing add-ins on dev), run the server-side live gate, then close 187. Tell ontology-r1 when it merges. |

## Task 187 (security, #1857): external reads check live parents
- **The finding** (ontology-r1, mapped by uac-r2): external lists admit a child row by its copied stamp only. Stale copies and "carrier" rows (permanent) list behind the D-123 wall. Affected: to-dos, events, PATCH/DELETE on to-dos, messages, notifications, invoice threads, document related links.
- **Owner round 99:** the live-parent check (precise), not the owner-team check.
- **Collision rule:** don't touch ExternalDataService's shared collection-page reader or `CollectionPrefer` (SPA-r3 task 064). SPA-r3's 045 WorkforceHome guard stays first in `WorkforcePrincipalResolver.ResolveAsync`.
- **Open:** invoice threads were "no rule" by owner option A. If the implementer reports a conflict with that decision, it goes to the owner.

## Other lanes
- **Ontology:**
  - #1854 (143 K1/K2): CLEAN at `d4a98260d`; they merge.
  - Task 144 resumes on guard option A with UAC's 5 conditions. UAC reviews its exemption commit in 144's PR.
  - Task 183 (#1797) closes after ontology's 148 live gate (412 → Conflict).
- **Provisioning:** owns 185's release gate: a CI-built SpaarkeMaster release carrying `sprk_externalinvitecodehash` before T240d production. They'll send the version number. T240d builds on `6da771b96`.
- **SPA-r3:** has master merged into R3. 046 (WorkforcePrincipalResolver and the binder), 047, 048 (systemuser home-link columns, owner-approved schema) and 058 come to UAC for review.
- **word-add-in:** the owner is UATing add-ins on dev at `6da771b96`. Their #1815 changes `Set-ExternalFlagForB2BGuests.ps1`. UAC's "no -Apply in Model 1" rule stays until the owner restates it.
- **Task 186 (baseline access only):** pending SPA-r3's principal contract. Tell provisioning when the Workforce role is final.
- **Issues filed this period:** #1813, #1817, #1830 (triaged), #1857 (task 187).

## Open owner items
- The live CIAM sign-ins: checklist §12 (185's code-gated bind) and §11 (184's ExistingAccount option).
- Restate the Model 1 `-Apply` rule once #1815 merges.
- Task 186 ceiling decision.
- #1747 and #1776.
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.

## Not started
178, 136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping.
