# Current Task State — ai-advanced-capabilities-analysis-hub-r1

> **Last Updated**: 2026-08-03 (by task-execute Step 11 — project close)
> **Recovery**: Read the Quick Recovery table first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none |
| **Status** | ✅ **PROJECT COMPLETE** — all 28 tasks ✅ |
| **Next Action** | Project complete. Optionally run `/devops-project-archive --status Completed` (closes the Project Issue; worktree preserved) and `/repo-cleanup`. |
| **Branch** | `work/ai-advanced-capabilities-analysis-hub-r1` — all hub work merged to master + deployed (client + BFF, hash-verified) |

### Closeout summary (task 090, 2026-08-03)
- **`/test-diet` gate**: run — 6 test files, all MAINTAIN, 0 scaffolding, 0 ambiguous, 0 path-violation. Report: [`notes/test-diet-report.md`](notes/test-diet-report.md).
- **9 Success Criteria**: all met — 071/072 UAT-confirmed by owner; my-side verification: retirement grep-clean (SC#7), record-open via `openSpaarkeAi` (SC#9), WorkspaceTabManager 67/67, widget suites 15/15, BFF Chat/Q2 35/35 (117 unit tests green total).
- **README**: marked ✅ Complete with 9 SC checked + changelog entry.
- **Lessons**: [`notes/lessons-learned.md`](notes/lessons-learned.md) — 4 corrections (A1 lookup naming, headless-full-workspace, un-merged-deploy overwrite, Q2 silent-FK) + 5 confirmed approaches.
- **TASK-INDEX**: all rows ✅.

### Deferred (owner-gated, out of code scope)
- **071 env work**: ribbon-button import via `/ribbon-edit` + retired `sprk_analysisworkspace` WR delete + Matter/Project form subgrid placement. Code + ribbon scripts shipped; env application is an owner action. UAT-confirmed 2026-08-03.

### Deployment (both hash-verified from latest master)
- Client `sprk_spaarkeai`: `pwsh scripts/Deploy-SpaarkeAi.ps1`.
- BFF `spaarke-bff-dev`: `pwsh scripts/Deploy-BffApi.ps1` — 48.25 MB, hash-verified, health OK.
- **Shared-env rule**: always merge → build-from-master → deploy → hash-verify. Never deploy un-merged to spaarkedev1.

---

## Full State (Detailed)

Project mission delivered: generalized the NDA vertical into a first-class **Analysis platform** — durable `sprk_analysis` spine + session↔Analysis binding (fork-on-analysis) + hub widget + per-type wizard + clean `sprk_analysisworkspace` retirement. ≈80% reuse. All 28 tasks (001–090) complete. Platform ↔ review-machine split with agreements-r1 (this project = substrate + A1–A6 contract; agreements-r1 = the review machine); consumer coexistence cross-validated.
