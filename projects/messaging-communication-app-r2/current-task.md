# Current State — messaging-communication-app-r2 (Communication Workspace)

> **Last Updated**: 2026-07-20
> **Status**: ✅ **PROJECT COMPLETE** — code-complete, merged to master, BFF deployed. No active task.

---

## Quick Recovery

| Field | Value |
|-------|-------|
| **Phase** | ✅ **COMPLETE** — all 21 tasks ✅; 8654 tests pass / 0 fail; publish ~46.24 MB (<60); 0 new CVE; 0 ADR violations. |
| **Merged** | ✅ **Merged to master** (2026-07-20). Branch `work/messaging-communication-app-r2` = master lineage. |
| **BFF** | ✅ Deployed + verified live on `spaarke-bff-dev` (`by-regarding` / `query` / `participant=` registered). |
| **Successor** | **R3 spun off** → `messaging-communication-app-r3` (spec + design on master; worktree `C:\code_files\spaarke-wt-messaging-communication-app-r3`). |
| **Active task** | **None** — project complete. |

## Continuity — nothing lost

All open findings, deferred decisions, and R3 prerequisites are recorded in the handoff ledger:
→ [`notes/r2-closeout-and-r3-handoff.md`](notes/r2-closeout-and-r3-handoff.md)

Summary: re-derive trigger → R3 FR-17 · unread field → R3 FR-25 · RegardingResolver catalog gap → deferred (inert; fix recipe preserved) · Compose-dep gap → portfolio-level. R2 UI surfaces superseded by R3 are intentionally not deployed.

## Owner follow-ups (R3 depends on these)
- Confirm task 002 (thread schema) + **task 003 (participant junction)** are live in Dataverse.
- Ensure the notification spine is available for R3 (FR-22).

## Key artifacts
`spec.md` · `design.md` · `README.md` · `tasks/TASK-INDEX.md` (all ✅) · `notes/r2-closeout-and-r3-handoff.md` · `notes/lessons-learned.md` · `notes/test-diet-report.md` · `.claude/adr/ADR-048-communication-participant-index.md`.
