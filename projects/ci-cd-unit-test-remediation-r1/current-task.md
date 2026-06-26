# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-26 (Phase 2 complete; Phase 3 cutover next — calendar-gated)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — Phase 2 complete; Phase 3 next (calendar-gated) |
| **Step** | — |
| **Status** | between-phases |
| **Mode** | 🤖 **AUTONOMOUS** — no approval gates (see CLAUDE.md) |
| **Next Action** | **Phase 3 = cutover + monitoring**. Task 070 (pre-cutover branch-protection snapshot) is ready to run BUT **branch protection is currently DISABLED on master** (per task 001 finding) — the snapshot will capture an "off" state. Decision needed before 071 cutover: do we (a) restore branch protection from Jun-1 baseline first, then cutover replaces with `CI / Router` only, OR (b) treat the cutover as a fresh setup using the Jun-1 baseline as reference. Both paths achieve the same end-state. **No urgency** — Phase 3 is intentionally gated by shadow-phase observation of the new tier workflows. Recommend: open a draft PR from `work/ci-cd-unit-test-remediation-r1` → master so the new workflows run in shadow against real PRs for 2-3 weeks of data collection before flipping branch protection. |

### Files Modified This Session (Phase 2 consolidated)

**Project artifacts**:
- All 12 Phase 2 task POMLs — statuses flipped (complete / complete-merged / complete-partial / cancelled-no-scope)
- `projects/ci-cd-unit-test-remediation-r1/tasks/TASK-INDEX.md` — Phase 2 status markers
- `projects/ci-cd-unit-test-remediation-r1/current-task.md` — this file

**Notes folder** (Phase 2 outputs):
- `notes/deploy-bff-api-trigger-audit.md` (task 044 — confirms master-trigger)
- `notes/path-reorganization-design.md` (task 050 — 3 strategy decisions documented for bulk-move follow-up)
- `notes/post-deletion-summary.md` (task 053 — what was deleted, path-check verified, build green)

**Persistent artifacts created** (Phase 2):
- `.github/workflows/ci-router.yml` (task 040 — 244 lines, single composite required check + alls-green aggregator)
- `.github/workflows/ci-tier1-blocking.yml` (task 041 — 421 lines, 5 jobs, 6-fact MUST-NOT NetArchTest subset)
- `.github/workflows/ci-tier2-advisory.yml` (task 042 — 612 lines, 8 jobs, PR-comment dedup verbatim)
- `.github/workflows/deploy-spaarke-ai.yml` (task 044 — Dataverse web resource deploy target)
- `scripts/validate-markdown-links.ps1` (task 044 — verified exit 0 clean / exit 1 broken)
- `tests/integration/{auth,regression,data-mutation,tenant,contract}/README.md` (task 050 — 5 path anchors)
- `tests/unit/domain/README.md` (task 050 — 1 path anchor; newly-created per spec UQ #3)

**Persistent files modified** (Phase 2):
- `.github/workflows/nightly-health.yml` (task 043 — +337 lines for Tier 3 augmentation: full-integration + coverage-observation + trivy-fs + dep-audit)
- `.claude/skills/task-execute/SKILL.md` (task 060 — Step 0.5 hot-path auto-invoke + Step 9.5 test-PR override)
- `.claude/skills/project-pipeline/SKILL.md` (task 061 — Step 2 INDEX.md overlap warning + Step 3 hot-path-declaration requirement)
- `.claude/constraints/bff-extensions.md` (task 062 — §G Hot-Path Declaration added)
- `CLAUDE.md` (root — task 062 — §8 test-modifying override row + §10 hot-path-declaration cross-ref + §17 three new pointer rows for ADR-038/TEST-ARCHITECTURE.md/INDEX.md)

**Deleted** (task 053):
- 11 wiring-test files under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` (9 HttpMessageHandler-mock + 2 DI-registration)

### Critical Context

**Phase 2 surfaced 3 findings affecting Phase 3 planning**:

1. **Branch protection currently DISABLED on master** (task 001 Phase 1 finding, re-emphasized here). Task 070 snapshot will capture an "off" state. Decision needed before 071: restore-then-cut-over (a) vs fresh-setup using Jun-1 baseline (b). Both achieve the same end-state (`CI / Router` as sole required check after cutover).

2. **Path reorganization (050) is scaffolded but bulk-moved deferred**. 6 canonical directories + READMEs exist; the path-MUST rules in `.claude/constraints/testing.md` are binding for NEW tests at canonical paths immediately. Existing 481 KEEP files remain at their current locations until a follow-up PR. This does NOT block Phase 3 cutover.

3. **Sub-slicing 053a/b/c collapsed to single 11-file PR**. Build verified green. Tasks 051 + 052 + 053c cancelled-no-scope per inventory finding. Critical path SERIAL-DEL chain shortens significantly.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | Phase 2 → Phase 3 transition |
| **Phase** | between (Phase 2 complete, Phase 3 calendar-gated) |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps

Phase 1 (13 tasks ✅) + Phase 2 (12 of 12 status-resolved):
- ✅ 12 implementation tasks done (040, 041, 042, 043, 044, 050-scaffolded, 053-collapsed, 060, 061, 062)
- 🚫 3 tasks cancelled-no-scope per inventory (051, 052, 053c)
- ✅ 2 tasks complete-merged into 053 PR (053a, 053b)

### Decisions Made (Phase 2)

- **2026-06-26**: Router signal model = Model A composite + `re-actors/alls-green@v1.2.2` aggregator (task 040)
- **2026-06-26**: Tier 1 NetArchTest MUST-NOT subset = 6 facts (ADR-001, ADR-002, ADR-007×2, ADR-009×2) — see task 041 report
- **2026-06-26**: Tier 2 dedup marker = `Tier 2 Advisory Report` (unique vs sdap-ci's `ADR Architecture Validation Report`)
- **2026-06-26**: SpaarkeAi deploy target = Dataverse web resource `sprk_spaarkeai` (NOT Azure Static Web App) — leverages existing `scripts/Deploy-SpaarkeAi.ps1` per ADR-026
- **2026-06-26**: Path reorganization bulk move DEFERRED with 3 documented strategy decisions (csproj architecture, namespace handling, PR sequencing) — see `notes/path-reorganization-design.md`
- **2026-06-26**: 053 sub-slicing COLLAPSED to single 11-file PR per inventory finding; 051, 052, 053c cancelled-no-scope

---

## Next Action

**Phase 3 cutover sequence (calendar-gated)**:

1. **Open draft PR from `work/ci-cd-unit-test-remediation-r1` → master** — lets the new tier workflows run in shadow against real PRs for 2-3 weeks of data collection
2. **Observe**: confirm Tier 1 p95 < 3 min (NFR-01), Tier 1 flake rate < 1% (NFR-03), Tier 2 p95 < 8 min (NFR-02) via gh API analytics on the shadow runs
3. **Task 070** (pre-cutover snapshot) — take `notes/branch-protection-pre-cutover.json` immediately before cutover
4. **Task 071** (cutover ~4h window) — restore Release matrix in sdap-ci.yml; flip branch protection to require only `CI / Router`; enable merge queue (batch=1, no speculative, 30min timeout)
5. **Task 075** (7-day soak gate) — observe surviving suite green ≥7 consecutive days
6. **Task 077** (sdap-ci retirement) — at cutover+14d minimum per spec MUST rule; delete sdap-ci.yml
7. **Task 076** (30-day SC measurements) — measure SC-01..SC-10 via gh API analytics at cutover+30d
8. **Task 090** (wrap-up) — README → Complete; lessons-learned.md; repo-cleanup

**Why calendar gates matter**: spec MUST rules require 7-day soak before Release matrix lock-in (SC-06) AND 14-day stability before sdap-ci retirement. These are NOT skippable. The cutover window itself is ~4 hours; the monitoring tail is ~30 days.

**Recommended action this session**: open the draft PR (`gh pr create --draft`) and let the new workflows accumulate shadow-mode data. Resume Phase 3 work after 2-3 weeks when there's enough run history to commit to cutover.

---

## Blockers

**Status**: None blocking. Phase 3 awaits calendar gates (shadow observation period; 7-day soak; 14-day stability) which are by design.

---

## Quick Reference

### Project Context
- **Project**: ci-cd-unit-test-remediation-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Worktree**: `c:\code_files\spaarke-wt-ci-cd-unit-test-remediation-r1\`
- **Branch**: `work/ci-cd-unit-test-remediation-r1`
- **Portfolio Issue**: [#457](https://github.com/spaarke-dev/spaarke/issues/457) (Epic [#429](https://github.com/spaarke-dev/spaarke/issues/429))

### Phase 2 deliverables shipped
- 5 new workflows: ci-router.yml, ci-tier1-blocking.yml, ci-tier2-advisory.yml, deploy-spaarke-ai.yml + augmented nightly-health.yml
- 3 skill-directive updates: task-execute, project-pipeline, conflict-check
- 1 constraint update: bff-extensions.md §G Hot-Path Declaration
- 1 root CLAUDE.md update: §8 rigor table override row + §10 hot-path cross-ref + §17 pointers
- 6 KEEP path scaffolds: tests/integration/{auth,regression,data-mutation,tenant,contract}/ + tests/unit/domain/
- 11-file deletion: wiring-test antipatterns removed from Sprk.Bff.Api.Tests/Services/Ai/
- 1 script: scripts/validate-markdown-links.ps1

---

*Phase 2 consolidated 2026-06-26. Phase 3 awaits calendar gates.*
