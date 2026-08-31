# Current Task State — spaarke-notification-spine-r2

> **Last Updated**: 2026-08-20 (project hydration by spaarke-notification-spine-r1)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | **Pre-spec** — investigation hydrated; no spec/plan/tasks yet. |
| **Status** | Investigation-only. No code, no tasks. |
| **Next Action** | Run `design-to-spec` with [`notes/INVESTIGATION-AND-ASSESSMENT.md`](notes/INVESTIGATION-AND-ASSESSMENT.md) as input; resolve its §9 open questions; then `project-pipeline` to generate spec/plan/tasks. |

### Critical context
This project rebuilds the proactive-suggestion **surface** (removed after r1 by `spaarkeai-assistant-enhancements-r2` FR-E1) as **OOB Dataverse notifications**: a scheduled BFF job → grounded+gated items → deduped against `sprk_notificationoutbox` (7-day window) → outbox ledger row + `appnotification` with a modal action (`navigationTarget:"dialog"`). Everything needed to author the spec is in `notes/INVESTIGATION-AND-ASSESSMENT.md`: the finding + evidence, gap analysis, six owner decisions (2026-08-20), researcher OOB findings, proposed architecture, component-justification pre-work, and seven open questions.

### Files created this session (hydration)
- `README.md` — project overview + draft graduation criteria.
- `CLAUDE.md` — project context + carried constraints.
- `notes/INVESTIGATION-AND-ASSESSMENT.md` — the hydrated substance (the deliverable).
- `current-task.md` — this file.

---

## Full State

Nothing executed. This is a hand-off from `spaarke-notification-spine-r1`, which:
- Built the spine backend (Layers A–D, ADR-047) — live on master.
- Discovered the r1 renderer was removed and reconciled the r1 notification docs to reality (2026-08-20).
- Hydrated this r2 folder with the investigation/assessment so the OOB-notifications feature can be spec'd next.

## Recovery Instructions
1. Read Quick Recovery + `notes/INVESTIGATION-AND-ASSESSMENT.md`.
2. To begin: run `design-to-spec` for this project (investigation input above).
3. This is a BFF-touching project — the eventual `design.md` needs a Placement Justification + Hot-Path Declaration (BFF=Y); `/conflict-check` before BFF PRs.
