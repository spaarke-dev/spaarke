# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: r2 project **COMPLETE** as of 2026-06-04 task 090. No active task. Pending main-session batch commit + push for the wrap-up deliverables.

---

## 🎯 Active task — none

r2 (Phase 1.5) is **complete**. Task 090 closed 2026-06-04 with:

| Deliverable | Path | Status |
|---|---|---|
| Lessons learned | `notes/lessons-learned.md` | ✅ authored |
| Phase 2 outline | `PHASE-2-OUTLINE.md` | ✅ authored (Tier 1: 4 items / Tier 2: 5 items / Tier 3: 4 items / Tier 4: 9 items) |
| README status flip | `README.md` | ✅ Status = "✅ Complete (Phase 1.5)" with PR + SC + lessons + Phase 2 refs |
| Ephemeral notes cleanup | `notes/archive/wave-b-action-codes-draft-2026-06-02.md` | ✅ archived; all canonical handoffs + spikes preserved |
| TASK-INDEX update | `tasks/TASK-INDEX.md` | ✅ task 090 marked ✅ |
| current-task.md reset | THIS file | ✅ reset to "complete; next action = open r3" |

---

## Next action (owner / main session)

1. **Batch-commit + push wrap-up changes** on branch `work/ai-spaarke-insights-engine-r2-wave-f` (or a new branch if owner prefers separate PR for wrap-up).
   - Files: `notes/lessons-learned.md`, `PHASE-2-OUTLINE.md`, `README.md`, `tasks/TASK-INDEX.md`, `current-task.md`, `notes/archive/wave-b-action-codes-draft-2026-06-02.md`, `notes/drafts/` (empty after archive)
   - Suggested commit message: `docs(insights-engine-r2): task 090 — Phase 1.5 wrap-up + lessons-learned + Phase 2 outline + archive`
2. **Confirm PR #339 (Wave F) merges cleanly** — CI re-running on master merge commit; auto-merge enabled. Wrap-up commit can ride on the same branch or be PR'd separately per owner direction.
3. **Per owner direction**: open follow-on project `ai-spaarke-insights-engine-r3` using `PHASE-2-OUTLINE.md` as primary design.md input. Owner picks initial wave scope from Tier 1 (recommended) or other tier per business priority.

---

## Project final state (post-task-090)

**Phase 1.5 acceptance bar**: hit. 14/15 spec.md SCs met; SC-15 (SME calibration ≥50 observations) explicitly carried to Phase 2.

**PRs shipped (5)**:

| PR | Wave | Title | Status |
|---|---|---|---|
| #330 | B | Wave B — synthesis unblock | ✅ merged |
| #334 | A + C | Foundations + JPS compliance | ✅ merged |
| #336 | D | 2D taxonomy + multi-entity | ✅ merged |
| #337 | E | Hybrid + Assistant integration | ✅ merged |
| #339 | F | Contract v1.1 — SSE + clickable citations | 🔄 in-flight (auto-merge on CI green) |

**Quality summary** (from `notes/lessons-learned.md` §4):
- Publish size: 44.13 MB (Wave F close), down 1.52 MB from Phase 5 baseline; well under 60 MB ceiling
- 0 new NuGet packages; 0 new HIGH-severity CVEs across all 5 waves
- §3.5 facade-grep gate clean on every wave
- 1 stuck-agent incident (Wave D task 032 — 12h hang); memory-encoded; 0 incidents in Waves E + F
- 3 CI flakes addressed (timing test, Post Cache race, FileSystemWatcher dispose NRE) — root-cause = pre-existing test infrastructure latencies, fixes shipped in PR #339

**Open items** (handed forward to r3 or operator):
- Operator: set `Insights:CitationHref:BffBaseUrl` config on each environment BEFORE smoke-testing v1.1 features (Dev: `https://spaarke-bff-dev.azurewebsites.net`) — per `insights-engine-r2-wave-f-shipped` memory note
- Operator: Assistant team review of `design-e3-tool-call-contract.md` v1.1 (sub-task A.5 of task 042)
- Operator: Assistant-side implementation of contract v1.1 (SSE + href)
- r3 Tier 1.1: `NullInsightsAi` facade — close asymmetric registration on `IInsightsAi`
- r3 Tier 1.3: Test-fixture hygiene cleanup — eliminate CI flake class via integration-test gate

---

## Memory references

- `insights-engine-r2-wave-b-complete` — Wave B closure (PR #330)
- `insights-engine-r2-wave-d-shipped` — Wave D ship + 12h hang lesson
- `insights-engine-r2-wave-e-shipped` — Wave E ship + asymmetric registration finding
- `insights-engine-r2-wave-f-shipped` — Wave F contract v1.1 ship
- `feedback-detect-stuck-subagents` — output-file mtime liveness check
- `feedback-parallel-execution` — dispatch parallel when TASK-INDEX says so
- `feedback-actual-deps-not-orchestration-groups` — use actual dep graph not wave-group labels
- `reference-spaarke-ci-format-gate` — `dotnet format whitespace verify` before push
- `spaarke-unmanaged-solutions` — ADR-027 mismatch with actual practice

These memories carry the operationally important lessons forward to r3.
