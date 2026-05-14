# Implementation Plan — AI Procedure Quality R1

> **Last Updated**: 2026-05-14
> **Spec**: [spec.md](spec.md)
> **Total tasks**: 24 + 1 wrap-up = 25
> **Phases**: 7 (Phase 0, 1, 2a, 2b, 3a, 3b, 4a, 4b, 5)
> **Human gates**: 2 (Phase 2b prerequisite + Phase 3b prerequisite)

## Architecture Context

### Discovered Resources

This project does NOT touch application code (`src/`). It modifies the AI procedure surface and CI/CD infrastructure. Applicable resources:

**ADRs**: None directly applicable (this is procedure-side, not architecture-side). The audit MUST preserve the ADR system as-is — no ADR content is rewritten.

**Skills involved (audit subjects)**: All 49 skills under `.claude/skills/`. The audit-subject inventory is part of Phase 0.

**Skills invoked during execution**:
- `ai-procedure-maintenance` — existing reactive checklist; we extend with proactive infrastructure
- `task-execute` — for each task in this project
- `code-review` (eventually, on the PR)
- `adr-check` (verify we didn't violate any ADRs)

**Knowledge / pattern docs**:
- [.claude/skills/INDEX.md](../../.claude/skills/INDEX.md) — current skill registry
- [.claude/skills/RIGOR-LEVEL-IMPLEMENTATION.md](../../.claude/skills/RIGOR-LEVEL-IMPLEMENTATION.md) — rigor-level decision tree (preserve, do not modify)
- [.claude/skills/SKILL-INTERACTION-GUIDE.md](../../.claude/skills/SKILL-INTERACTION-GUIDE.md) — preserve, may extend after Phase 2

**Templates available**:
- `.claude/templates/current-task.template.md`
- `.claude/templates/project-README.template.md`
- `.claude/templates/project-plan.template.md`
- (Phase 1 adds) `.claude/skills/_template/SKILL.md` for new skills

**Scripts available / extended**:
- `scripts/quality/post-edit-lint.sh` (existing)
- `scripts/quality/task-quality-gate.sh` (existing)
- (Phase 4a adds) 4 new validators + orchestrator
- (Phase 5 adds) monthly schedule

**Failure-mode evidence** (the load-bearing rationale):
- 2026-05-14 SDV bundle regression — commits `c132773c`, `36754b3a`
- 2026-05-14 settings.json schema malformation — commit `8ca796ab`
- 2026-05-14 BFF deploy health-check window false-failure — commit `6d7bcf45`
- 3 GHA workflows failing in 0s — to be diagnosed in Phase 4b

---

## Phase Breakdown

### Phase 0 — Inventory + Baseline (autonomous, ~2-3h, parallelizable: yes)

**Goal**: Capture current state of every piece this project will touch. No changes yet.

**Deliverables**:
- `notes/inventory/skills.md` — all 49 skills with description, body line count, frontmatter fields, presence of Gotchas, supporting subdirectories
- `notes/inventory/claudemd.md` — section breakdown of root CLAUDE.md (currently 1190 lines)
- `notes/inventory/workflows.md` — all `.github/workflows/*.yml` with status (passing/failing/unused) and event triggers
- `notes/inventory/settings-state.md` — schema-validate `.claude/settings.json` + `.mcp.json`; note any drift from documented schema
- `notes/inventory/baseline-pcf-bundle-sizes.md` — committed bundle.js sizes for each PCF (the reference for Phase 4a bundle-size guard)

**Parallel groups**:
- Group 0-A: skills inventory + claudemd inventory + workflows inventory + settings inventory → 4 parallel agents
- Group 0-B (after 0-A): baseline-pcf-bundle-sizes (sequential, depends on knowing what to baseline)

**Tasks**:
- 001-inventory-skills
- 002-inventory-claudemd
- 003-inventory-workflows
- 004-inventory-settings
- 005-baseline-pcf-bundle-sizes

---

### Phase 1 — Additive Infrastructure (autonomous, ~2h, parallelizable: yes)

**Goal**: Create new artifacts that don't touch anything existing. Pure additions are reversible by deletion; no archives needed.

**Deliverables**:
- `.claude/agents/researcher.md` — subagent per Directive 1, reads `spaarke/knowledge/` first, model `opus`, effort `high`
- `.claude/skills/_template/SKILL.md` — canonical skill scaffold (description, when-to-use, what-to-do, gotchas, references, opt-in `exemplar:` field, `Last Reviewed:` stamp)
- `.claude/CHANGELOG.md` — forward-only procedure-surface changelog; first entry is this project
- `.claude/FAILURE-MODES.md` — repo-level catalog; inaugural entries are the four 2026-05-14 issues
- `.claude/archive/.gitkeep` — establish archive directory convention

**Parallel groups**:
- Group 1-A: all 5 artifacts → 5 parallel agents (each artifact is independent)

**Tasks**:
- 010-create-researcher-subagent
- 011-create-skill-template
- 012-create-claude-changelog
- 013-create-failure-modes-catalog
- 014-establish-archive-convention

---

### Phase 2a — Skills Audit (autonomous, ~3-4h, parallelizable: yes)

**Goal**: Produce `AUDIT-FINDINGS-SKILLS.md` with per-skill assessment against the 7 best practices in spec.md. No changes to skills yet.

**Deliverables**:
- `.claude/AUDIT-FINDINGS-SKILLS.md` with one section per skill containing:
  - Description quality (pass / needs revision / vague)
  - Body line count + whether splitting needed (200-line target)
  - Goal-oriented vs prescriptive
  - Gotchas section present / missing / stub
  - Overlap with other skills (list any)
  - Deterministic rules to extract (to settings/hooks)
  - References to verify (file paths, URLs)
  - Recommended `exemplar:` value (path OR `none-too-volatile` with rationale)
  - Recommended action: refine in place / split / merge with skill X / remove / leave alone

**Parallel groups**:
- Group 2a-A: audit 49 skills, batched 6 per wave (max concurrency per pipeline rules)
  - Wave 1: skills 1-6
  - Wave 2: skills 7-12
  - ... through Wave 9 (skills 49)
- Each wave verifies build still clean before dispatching next wave (per pipeline rules — even though we're not modifying code, this is a discipline check)

**Tasks**:
- 020-audit-skills-batch-1 (skills 1-6)
- 021-audit-skills-batch-2 (skills 7-12)
- ... (one task per batch of 6)
- 028-audit-skills-consolidate (compile per-batch findings into single AUDIT-FINDINGS-SKILLS.md)

---

### 🚪 Human Gate 1 — Review AUDIT-FINDINGS-SKILLS.md

**Owner**: Human reviewer. **Blocks**: Phase 2b. **Expected duration**: 1-2 hours.

Reviewer signs off on each per-skill recommendation. Marks any **destructive** action (remove / merge) as ✅ or ❌. Marks **refine in place** as auto-approved. Reviewer also resolves any conflicts the audit flagged.

Sign-off mechanism: a `Reviewer-Approved: <name> <date>` line added to each per-skill section in `AUDIT-FINDINGS-SKILLS.md`.

---

### Phase 2b — Skills Refinement (autonomous, ~2-4h, parallelizable: yes)

**Goal**: Apply approved refinements. Archive removed/merged skills.

**Deliverables**:
- Each skill marked "refine in place" rewritten per its audit recommendation
- Each skill marked "split" has its body trimmed to <200 lines; detailed content moved to `references/` or `examples/` subdirectories
- Each skill marked "merge" combined into the target skill; originals moved to `.claude/archive/2026-05-14/skills/<name>/`
- Each skill marked "remove" moved to `.claude/archive/2026-05-14/skills/<name>/`
- Every remaining skill has a Gotchas section (stub acceptable)
- Every remaining skill has an `exemplar:` frontmatter field
- Every remaining skill has a `Last Reviewed: 2026-05-14` stamp

**Parallel groups**:
- Group 2b-A: refine-in-place tasks (one agent per skill, batched 6 per wave)
- Group 2b-B (sequential): merge tasks (requires source + target both in known state)
- Group 2b-C: archive tasks (single agent, sequential file moves)

**Tasks**:
- 030-refine-skills-batch-1 (parallel wave)
- ... (one task per wave)
- 037-apply-skill-merges (sequential, single task)
- 038-archive-removed-skills (sequential, single task)

---

### Phase 3a — CLAUDE.md Audit (autonomous, ~1-2h)

**Goal**: Produce `AUDIT-FINDINGS-CLAUDEMD.md` with section-by-section disposition + target outline. No rewrite yet.

**Deliverables**:
- `.claude/AUDIT-FINDINGS-CLAUDEMD.md` with:
  - Current section breakdown of CLAUDE.md (1190 lines today)
  - For each section: classification (keep / move-to-skill / move-to-settings / move-to-docs / move-to-knowledge / delete)
  - Cross-reference verification (does each path in CLAUDE.md resolve?)
  - Duplication detection (cross-referenced against Phase 2 skill audit)
  - Target outline for new CLAUDE.md (4 sections, <200 lines)
  - List of new skills to create (if workflow content is being extracted)
  - List of settings.json changes (if deterministic rules being extracted)

**Sequential** (one file, one audit).

**Tasks**:
- 040-audit-claudemd

---

### 🚪 Human Gate 2 — Review AUDIT-FINDINGS-CLAUDEMD.md

**Owner**: Human reviewer. **Blocks**: Phase 3b. **Expected duration**: 30-60 minutes.

Reviewer approves target outline + section dispositions. Sign-off via `Reviewer-Approved: <name> <date>` line.

---

### Phase 3b — CLAUDE.md Rewrite (autonomous, ~1-2h)

**Goal**: Replace CLAUDE.md with the approved target outline. Move extracted content to its new homes.

**Deliverables**:
- Archive current CLAUDE.md to `.claude/archive/2026-05-14/CLAUDE.md`
- New CLAUDE.md following approved 4-section structure (<200 lines)
- Each extracted section moved to its destination (new skills, settings.json updates, docs/architecture, docs/guides, knowledge/)
- All cross-references in new CLAUDE.md verified to resolve
- Verification session: fresh Claude Code session can reference the new structure correctly

**Sequential**.

**Tasks**:
- 050-rewrite-claudemd
- 051-verify-claudemd-rewrite

---

### Phase 4a — `.claude/` Standing Infrastructure (autonomous, ~3-4h, parallelizable: yes)

**Goal**: Build the 4 validators + CI integration.

**Deliverables**:
- `scripts/quality/Validate-ClaudeSettings.ps1` — JSON schema check for `settings.json`, `settings.local.json`, `.mcp.json`. <3s.
- `scripts/quality/Find-PatternDrift.ps1` — walk `.claude/patterns/**/*.md`, extract referenced code paths, verify they exist. Report missing/moved.
- `scripts/quality/Test-ReferenceExemplars.ps1` — for each skill with opted-in `exemplar:`, run the skill's prescribed build/deploy and verify output matches committed reference within tolerance.
- `scripts/quality/Check-BundleSizeDrift.ps1` — walk every PCF's committed `bundle.js`, record expected size in JSON, warn when fresh build deviates >20%.
- `.github/workflows/procedure-quality.yml` — runs validators on PRs that touch `.claude/`, `scripts/quality/`, or any reference exemplar source. <30s total.

**Parallel groups**:
- Group 4a-A: 4 validators in parallel (each is independent)
- Group 4a-B: GHA workflow that orchestrates them (sequential after 4a-A)

**Tasks**:
- 060-validator-claudesettings
- 061-validator-patterndrift
- 062-validator-exemplars
- 063-validator-bundlesize
- 064-gha-procedure-quality

---

### Phase 4b — GitHub Actions Hardening (autonomous, ~2-3h, parallelizable: yes)

**Goal**: Fix the 3 failing workflows; add actionlint; pin to SHAs; document notification routing.

**Deliverables**:
- 3 currently-failing workflows (`sdap-ci.yml`, `deploy-promote.yml`, `deploy-infrastructure.yml`) diagnosed; either fixed or explicitly disabled with rationale in PR description
- `.github/workflows/actionlint.yml` — runs `actionlint` on PRs touching `.github/workflows/**`. <30s.
- Pre-commit hook: `scripts/quality/Validate-Workflows.ps1` invoking `actionlint`. <3s.
- All workflow action references pinned to commit SHAs (not version tags)
- `.github/dependabot.yml` updated to include `package-ecosystem: github-actions` for auto-bump-and-test
- `docs/procedures/github-notification-routing.md` — step-by-step user-side instructions for routing `spaarke-dev` notifications to `dev@spaarke.com`
- Re-audit of required status checks; align with workflows that actually pass

**Parallel groups**:
- Group 4b-A: workflow diagnosis + actionlint + notification doc + Dependabot config + required-checks audit → 5 parallel agents (each touches different files)
- Group 4b-B (sequential after 4b-A): SHA pinning across all workflow files (single agent, single PR)

**Tasks**:
- 070-diagnose-failing-workflows
- 071-add-actionlint
- 072-pin-actions-to-shas
- 073-notification-routing-doc
- 074-dependabot-actions-config
- 075-audit-required-checks

---

### Phase 5 — Recurring Cadence (autonomous, ~1-2h)

**Goal**: New skill + monthly schedule + first quality-audit report.

**Deliverables**:
- `.claude/skills/procedure-quality-audit/SKILL.md` — single-entrypoint audit. Runs all 4 validators + reference-exemplar tests + manual spot-check of 3 random opted-out skills + CLAUDE.md line-count check.
- `scripts/quality/Run-ProcedureQualityAudit.ps1` — orchestrator script the skill invokes.
- `scripts/quality/Schedule-MonthlyQualityAudit.ps1` — monthly cron via `/schedule` or equivalent; produces `.claude/QUALITY-AUDIT-<date>.md`.
- First run of audit produces inaugural report.
- `docs/procedures/procedure-quality-cadence.md` — operational doc: when reports are produced, how to triage, who acts on findings.

**Sequential** (each step builds on prior).

**Tasks**:
- 080-procedure-quality-audit-skill
- 081-run-procedure-quality-orchestrator
- 082-schedule-monthly-audit
- 083-cadence-procedure-doc
- 084-run-inaugural-audit

---

### Wrap-up (autonomous, ~30 min)

**Tasks**:
- 090-project-wrap-up — update README status to Complete, create `notes/lessons-learned.md`, archive project artifacts, prompt user for `/merge-to-master`

---

## Critical Path

```
Phase 0 (parallel) → Phase 1 (parallel)
                  → Phase 2a (parallel batched) → 🚪 Human Gate 1 → Phase 2b (parallel batched)
                                                                  → Phase 3a (serial) → 🚪 Human Gate 2 → Phase 3b (serial)
                                                                                                       → Phase 4a (parallel) ─┐
                                                                                                       → Phase 4b (parallel) ─┤
                                                                                                                              → Phase 5 (serial) → Wrap-up
```

Phases 4a and 4b can start in parallel with Phase 2 (they don't depend on the audit results). Conservative scheduling: gate them on Phase 1 completion only.

## Risks

See spec.md "Risks and Mitigations" section. Highest risks:
1. **Audit fabricates problems** — mitigated by NF-2 (honesty requirement) + Human Gate 1
2. **CLAUDE.md rewrite removes used content** — mitigated by Human Gate 2 + archive-not-delete
3. **CI validators block legitimate PRs** — mitigated by introducing as warnings first, promoted to errors after 1 week of clean signal
4. **The recurring cadence becomes shelf-ware** — mitigated by F-14 requiring human action within 1 week of each monthly report

## References

- [spec.md](spec.md) — full specification with 20 functional requirements
- [design.md](design.md) — claude.ai consultation input
- [README.md](README.md) — project overview
- [notes/additional-best-practices.md](notes/additional-best-practices.md) — deferred items + R2 candidates
