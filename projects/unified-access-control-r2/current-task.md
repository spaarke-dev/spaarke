# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–97) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-13 (checkpoint #31).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path; file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | (a) The 185 fix: `AssignedAccessMaterializer` sends the invite on internal assignments (I-9), in `C:/wt185`. (b) Adversarial pass 1 of 2 on PR #1827 at `df4ce6825`. |
| **Next Action** | Fold pass 1's F findings and the I-9 fix into one fix round, then run pass 2 on the full diff. Then merge #1827 (merge-tree check, `--match-head-commit`, no `--delete-branch`) once Router is green, deploy to dev, and run the server-side live gate. The dev schema is already applied (VERIFY PASS). Then close 185 and tell provisioning and SPA-r3. |

## Active lanes
- **Task 183 (#1797):** merged into `docs/ontology-platform-design` (`4053387fe`). It closes once ontology's 148 live gate on dev shows a stale-ETag 412 → Conflict on both Signal and DR.
- **Ontology:** #1805 is merged (`d6b952bdf`). #1824 (task 142) re-checked CLEAN at `226003f36`; they merge once all checks are green. Task 143 comes next for UAC review. Filed: #1813, #1817, #1818 and #1825.
- **#1807 (ontology):** `a4f0b3b07` is CLEAN; D-136 is live. Ontology owns the rest (memory: peer-owned-decisions).
- **SPA-r3 (ADR-028 A9):** aligned and confirmed. They build on #1801 (now on master). UAC reviews their PRs against the conditions in the project CLAUDE.md §3 "Model 1 sign-in" block: a new binder branch with no E10/E11 change; the stored link in its own field; word-add-in findings (a), (b), (d) and (e).
- **Task 186 (baseline access only):** pending. It needs SPA-r3's workforce-contact principal (at least its contract). Design draft: `notes/186-model1-workforce-design.md`; it recommends the role-as-ceiling option (a). The owner decision is still to come. Intake 403: reference SPA-r3's I-6 decision; do not ask again.
- **Task 185:** PR #1827 (slices A, B1, B2, C, D, E, F). Gates are green, publish size +18 KB, and the dev schema is applied. Provisioning is reviewing it as the T240d gate; SPA-r3 rebases after it merges. Decisions: design note §11 and §12 (I-1 to I-9).

## Done this period
- Task 184 CLOSED: #1801 `06b3654a0`, deployed; live gate G1/G2 PASS. G3 (ExistingAccount live) and the live E11 bind are on the owner checklist §11.
- #1746 merged (`376eca286`).
- The master merge `30f4a6e4e` (932 commits) brought ADR-028 A6–A8; A9 was aligned in `15fb8cf6b`, and 186 was rescoped.
- Issues filed: #1802, #1803 and #1804.

## Open owner items
- Task 186 ceiling decision, once designed.
- Owner checklist §11: (b) is owned by provisioning as T240d live acceptance; the live E11 sign-in is still yours.
- #1747 (word-add-in sign-off) and #1776 (Dev 1 and Test 1 BU team roles).
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.
- Optional: file an issue for the legacy `spaarke_core` solution; update `scripts/README.md`.

## Not started
178, 136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping.
