# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–97) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-13 (checkpoint #28).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path; file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | Task 184 live-gate agent (G1 App Insights, G2 AlreadyProvisioned re-invite of `394fda9f…`, G3 ExistingAccount via ONE throwaway contact that is deactivated afterwards, G4 marker FLS read). It appends "## #1801 / task 184 live gate (2026-10-13)" to `notes/batch5-live-gates-2026-10-08.md` without committing. |
| **Next Action** | When the gate reports: check that G3's throwaway contact was deactivated. If G1–G3 PASS: commit the notes, close 184 (POML status, TASK-INDEX ✅, `scripts/check-task-status-drift.ps1`, board sync), and put any NOT AVAILABLE functional checks on the owner checklist (G8). If anything FAILs: run one fix round per round 90. |

## Active lanes
- **Task 184 (#1801):** merged `06b3654a0`; deployed to dev at about 10:55 on 2026-10-13 (SHA verified, healthz 200, CORS OK). The schema is applied on dev (`-Verify` PASS). The live gate is running.
- **Task 183 (#1797):** merged into `docs/ontology-platform-design` (`4053387fe`). It closes once ontology's 148 live gate on dev shows a stale-ETag 412 → Conflict on both Signal and DR.
- **#1805 (ontology):** commit 1 is CLEAN. Ontology is folding in the all-tables fault split, K1, F2, F3 and the K2 notes. Review their fix-round diff when it arrives.
- **#1807 (ontology):** `a4f0b3b07` is CLEAN; D-136 is live. Ontology owns the rest (memory: peer-owned-decisions).
- **SPA-r3 (ADR-028 A9):** aligned and confirmed. They build on #1801 (now on master). UAC reviews their PRs against the conditions in the project CLAUDE.md §3 "Model 1 sign-in" block: a new binder branch with no E10/E11 change; the stored link in its own field; word-add-in findings (a), (b), (d) and (e).
- **Task 186 (baseline access only):** pending. It needs SPA-r3's workforce-contact principal (at least its contract). Design draft: `notes/186-model1-workforce-design.md`; it recommends the role-as-ceiling option (a). The owner decision is still to come. Intake 403: reference SPA-r3's I-6 decision; do not ask again.
- **Task 185 (invite-code proof for the E11 email bind):** pending. Design note first, then owner review. It is provisioning's T240d production gate.

## Done this period
- #1746 merged (`376eca286`).
- The master merge `30f4a6e4e` (932 commits) brought ADR-028 A6–A8; A9 was aligned in `15fb8cf6b`, and 186 was rescoped.
- Issues filed: #1802, #1803 and #1804.

## Open owner items
- Task 186 ceiling decision, once designed.
- Task 185 design review.
- #1747 (word-add-in sign-off) and #1776 (Dev 1 and Test 1 BU team roles).
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.
- Optional: file an issue for the legacy `spaarke_core` solution; update `scripts/README.md`.

## Not started
178, 136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping.
