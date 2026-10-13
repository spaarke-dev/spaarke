# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–97) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-13 (checkpoint #29).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path; file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | Task 185 design-note agent (writes `notes/185-invite-code-design.md`, uncommitted). |
| **Next Action** | When the 185 note lands: review it (the hash choice and KV key, the grant-leg choice, SPA-r3 overlap), commit it, send it to provisioning and SPA-r3, and put the owner questions to the owner. |

## Active lanes
- **Task 183 (#1797):** merged into `docs/ontology-platform-design` (`4053387fe`). It closes once ontology's 148 live gate on dev shows a stale-ETag 412 → Conflict on both Signal and DR.
- **#1805 (ontology):** final round `00f3aef42` re-checked CLEAN; ontology merges once CI is green. Two non-blocking K items were sent (message wording for an ambiguous subject; meter isolation in one test). #1813 and #1817 filed.
- **#1807 (ontology):** `a4f0b3b07` is CLEAN; D-136 is live. Ontology owns the rest (memory: peer-owned-decisions).
- **SPA-r3 (ADR-028 A9):** aligned and confirmed. They build on #1801 (now on master). UAC reviews their PRs against the conditions in the project CLAUDE.md §3 "Model 1 sign-in" block: a new binder branch with no E10/E11 change; the stored link in its own field; word-add-in findings (a), (b), (d) and (e).
- **Task 186 (baseline access only):** pending. It needs SPA-r3's workforce-contact principal (at least its contract). Design draft: `notes/186-model1-workforce-design.md`; it recommends the role-as-ceiling option (a). The owner decision is still to come. Intake 403: reference SPA-r3's I-6 decision; do not ask again.
- **Task 185 (invite-code proof for the E11 email bind):** pending. Design note first, then owner review. It is provisioning's T240d production gate.

## Done this period
- Task 184 CLOSED: #1801 `06b3654a0`, deployed; live gate G1/G2 PASS. G3 (ExistingAccount live) and the live E11 bind are on the owner checklist §11.
- #1746 merged (`376eca286`).
- The master merge `30f4a6e4e` (932 commits) brought ADR-028 A6–A8; A9 was aligned in `15fb8cf6b`, and 186 was rescoped.
- Issues filed: #1802, #1803 and #1804.

## Open owner items
- Task 186 ceiling decision, once designed.
- Task 185 design review.
- Owner checklist §11: (b) is owned by provisioning as T240d live acceptance; the live E11 sign-in is still yours.
- #1747 (word-add-in sign-off) and #1776 (Dev 1 and Test 1 BU team roles).
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.
- Optional: file an issue for the legacy `spaarke_core` solution; update `scripts/README.md`.

## Not started
178, 136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping.
