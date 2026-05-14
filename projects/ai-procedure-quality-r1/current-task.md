# Current Task State — AI Procedure Quality R1

> **Last Updated**: 2026-05-14 (by context-handoff, pre-compaction)
> **Recovery**: Read "Quick Recovery" section first — everything you need to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | ai-procedure-quality-r1 |
| **Branch** | `work/ai-procedure-quality-r1` |
| **PR** | [#294 (draft)](https://github.com/spaarke-dev/spaarke/pull/294) |
| **Latest commit** | `41088eff` — refinements: tiered targets + cross-ref + 3-depth review |
| **Status** | Setup complete; ready for execution |
| **Next Action** | Begin **Phase 0 wave 0-A**: dispatch tasks `001`, `002`, `003`, `004` as 4 parallel sub-agents via `task-execute` (one Skill tool call per task, all in a single message). |
| **Execution scope** | Phase 0 → Phase 1 → Phase 2a → **stop at Human Gate 1** (AUDIT-FINDINGS-SKILLS.md review) |

### How to resume after `/compact`

1. Just say: **"continue Phase 0 of ai-procedure-quality-r1"** or **"work on task 001"**.
2. The auto-detection in root CLAUDE.md routes "work on task X" to the `task-execute` skill.
3. Each task POML at `projects/ai-procedure-quality-r1/tasks/NNN-*.poml` is self-contained — it lists its goal, constraints, knowledge files, steps, and acceptance criteria.

### Critical Context

The project hardens the Claude Code AI procedure surface against drift. Triggered by 4 reactive bugs in the 2026-05-14 session that exposed the cost of having no proactive infrastructure (skill said wrong build cmd → 10× bundle regression; settings.json schema malformed 2 months → permission rules silently disabled; bff-deploy 60s window false-failed Linux; 3 GHA workflows failing in 0s on every push).

Scope: `.claude/skills/`, `CLAUDE.md`, `.claude/settings.json`, `.github/workflows/`, `scripts/quality/`. NO application source changes. Reversibility via `.claude/archive/2026-05-14/`.

---

## Execution Plan Summary

### Phase 0 — Inventory + Baseline (current — 0 of 6 complete)

Wave **0-A** (4 parallel sub-agents, ~1h wall-clock):
- [ ] `001-inventory-skills.poml` — walk `.claude/skills/`, capture frontmatter + line counts + presence of Gotchas/exemplar/Last Reviewed
- [ ] `002-inventory-claudemd.poml` — section breakdown of 1190-line root CLAUDE.md
- [ ] `003-inventory-workflows.poml` — workflow files + pass/fail status + action version pins
- [ ] `004-inventory-settings.poml` — schema-validate `.claude/settings.json`, `.local.json`, `.mcp.json`

Wave **0-B** (sequential after 0-A, 2 tasks):
- [ ] `005-baseline-pcf-bundle-sizes.poml` — record committed PCF `bundle.js` sizes (baseline for Phase 4a guard)
- [ ] `006-build-skill-crossref-map.poml` — 7-surface cross-reference matrix (required input to Phase 2b)

### Phase 1 — Additive Infrastructure (after Phase 0, 5 parallel, ~30min)

- [ ] `010-create-researcher-subagent.poml` — `.claude/agents/researcher.md` (opus, effort: high, reads `spaarke/knowledge/`)
- [ ] `011-create-skill-template.poml` — `.claude/skills/_template/SKILL.md`
- [ ] `012-create-claude-changelog.poml` — `.claude/CHANGELOG.md` (forward-only)
- [ ] `013-create-failure-modes-catalog.poml` — `.claude/FAILURE-MODES.md` with 4 inaugural entries
- [ ] `014-establish-archive-convention.poml` — `.claude/archive/2026-05-14/`

### Phase 2a — Skills Audit (8 waves × 6 skills + 1 consolidate, ~3-4h)

- [ ] `020-audit-skills-batch-1.poml` through `027-audit-skills-batch-8.poml` — 8 waves, 6 skills each (49 total). Each wave dispatches 6 parallel sub-agents. Sub-agents are read-only on `.claude/`; they return findings as text; main session writes batch findings to `notes/audit/batch-N.md`.
- [ ] `028-audit-skills-consolidate.poml` — consolidate 8 batches into `.claude/AUDIT-FINDINGS-SKILLS.md` ready for Human Gate 1 review.

### 🚪 STOP HERE — Human Gate 1

Reviewer signs off on `.claude/AUDIT-FINDINGS-SKILLS.md` per-skill recommendations before Phase 2b refinements run.

### Phases 2b through 5 + Wrap-up — DEFERRED

Not started in this execution scope. Will resume after Human Gate 1 sign-off in a separate session.

---

## Resolved Decisions (do not re-litigate)

1. **Cadence**: monthly procedure-quality-audit (1st of month or first business day)
2. **CI hosting**: pre-commit (<3s) + GHA (<30s) both. High-volume CI/CD friction budget is non-negotiable.
3. **Researcher subagent**: opus / effort: high. Reads from `spaarke/knowledge/` before searching externally.
4. **Reference exemplars**: opt-in per skill via frontmatter (`exemplar: <path>` OR `exemplar: none-too-volatile` with rationale). The audit harness re-builds only opted-in; opted-out are quarterly manual spot-checks.
5. **CHANGELOG**: forward-only from this project. No back-fill.
6. **Tiered line-count targets** (F-15.1):
   - `CLAUDE.md`: **<200 lines** hard target (always-on, every line costs every session)
   - `SKILL.md` body: **~200** target / **~400** cap with justification
   - Skill subdirectories (`references/`, `examples/`, `scripts/`): no limit (loaded on demand)
7. **7 audit recommendations** (was 5): `refine-in-place`, `split`, `merge-with-X`, `remove`, `leave-alone`, `leave-alone-justified` (over-target but warranted), `needs-substantive-rewrite` (structure OK but content needs focused follow-up, e.g., the 777-line `dataverse-create-schema`)
8. **F-15.2 Cross-ref map mandatory**: every destructive Phase 2b action MUST query `notes/inventory/skill-cross-refs.json` BEFORE and update all 7 referencing surfaces AFTER. `Find-SkillReferenceDrift.ps1` must exit 0 after each batch.
9. **F-15.3 Three-depth reference review**:
   - **Light** (mandatory, all 49 skills): file paths + URLs + skill names resolve
   - **Medium** (recommended, ~15 ops-heavy skills): command syntax sanity + code example accuracy. If >10min would be needed, flag `needs-substantive-rewrite` and skip.
   - **Heavy** (opt-in via `exemplar:`): full operational rebuild test
10. **Branch protection bypass acceptable** during this project (admin-bypass). Re-audit required status checks in task 075.

---

## Critical Constraints (apply to every task)

| | |
|---|---|
| **Reversibility** | Every removal/replacement goes to `.claude/archive/2026-05-14/<original-path>` BEFORE the file is changed. NEVER `rm` from git history. |
| **No application source changes** | `src/` is off-limits except for skills' code pointers (read-only verification). |
| **Sub-agent write boundary** | Sub-agents launched via Agent/task-execute CANNOT write to `.claude/`. Pattern: sub-agents READ skills + return findings as text; main session WRITES `.claude/AUDIT-FINDINGS-SKILLS.md` and similar. Tasks that modify `.claude/` are marked `parallel-safe: false`. |
| **Build verification between waves** | After each parallel wave, `dotnet build src/server/api/Sprk.Bff.Api/` even though we're not modifying app source — discipline check per /project-pipeline rules. |
| **Max concurrency** | 6 sub-agents per wave (hard limit). |
| **Honesty in audits** | If a skill is in good shape, the audit says so. Do not fabricate problems. |

---

## Files in This Project

- `spec.md` — 20 FRs + 4 new (F-15.1 through F-15.4) + 6 NFRs across 7 phases
- `plan.md` — phased WBS with parallel groups, dependencies, human gates, critical path
- `CLAUDE.md` — project-scoped agent context (constraints, sub-agent boundary, failure modes)
- `README.md` — graduation criteria + 4 failure-mode evidence rows
- `design.md` — claude.ai consultation directives (preserved verbatim)
- `tasks/TASK-INDEX.md` — 32 tasks with parallel groups + critical path + dependencies
- `tasks/001..090-*.poml` — 32 task POMLs (24 implementation + 5 cadence + 1 wrap-up + 2 new validators)
- `notes/additional-best-practices.md` — deferred-to-R2 reasoning record
- `notes/inventory/` — Phase 0 outputs land here (empty until execution starts)
- `notes/audit/` — Phase 2a per-batch findings land here (empty until execution starts)

---

## Files Modified This Session

(Setup phase only — no execution work yet)
- `projects/ai-procedure-quality-r1/spec.md` — drafted then refined with F-15.1–F-15.4
- `projects/ai-procedure-quality-r1/README.md`, `plan.md`, `CLAUDE.md`, `current-task.md` — generated
- `projects/ai-procedure-quality-r1/tasks/TASK-INDEX.md` + 32 task POMLs — generated
- `projects/ai-procedure-quality-r1/notes/additional-best-practices.md` — generated
- `projects/ai-procedure-quality-r1/design.md` — saved claude.ai input verbatim

All committed in 3 commits to `work/ai-procedure-quality-r1`:
1. `c39007e7` (on master) — initial project skeleton: spec + design + best-practices notes
2. `d40ad467` (on master) — spec updates: 5 new FRs (F-16…F-20) for GitHub Actions hardening + resolved decisions
3. `e74b134d` → `41088eff` (on work/ai-procedure-quality-r1) — README/plan/CLAUDE.md/tasks generation + tiered targets refinement

---

## Decisions Made This Session

Captured in committed files (not in conversation). See `spec.md` "Resolved Decisions" section and "Background — The Four Issues Surfaced 2026-05-14".

---

## Ready State

✅ Branch checked out: `work/ai-procedure-quality-r1`
✅ Working tree clean (only ignored artifacts modified — `.claude/settings.local.json`, build artifacts under `deploy/api-publish/`)
✅ Master sync: 0 commits behind origin
✅ PR #294 open and current
✅ Spec + plan + tasks committed
✅ All design decisions resolved and codified
✅ Sub-agent permission boundary understood (no `.claude/` writes from sub-agents)
✅ Friction budgets defined (<3s pre-commit, <30s GHA, monthly heavyweight)

**Ready for `/compact` and Phase 0 execution.**
