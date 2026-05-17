# Procedure-Surface Changelog

> **Forward-only from 2026-05-14.** No back-fill from history.

This file tracks changes to the agent-procedure surface — `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, and the root `CLAUDE.md`. Git history covers everything; this file is the **curated** view that a human (or future agent) can scan to answer "when did skill X change?" or "when did hooks last get fixed?" without bisecting commits.

Format follows [Keep a Changelog](https://keepachangelog.com/) conventions.

---

## How to maintain this

**Every PR that touches** `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, or the root `CLAUDE.md` **MUST add an entry to the `[Unreleased]` section below** before merge.

- One entry per logical change. Cite the commit SHA or PR number.
- Use the categories: **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed**.
- "Bumped version" and trivial typo fixes can be omitted.
- When a project releases (a `work/<project>` branch merges to master), promote `[Unreleased]` to `[<project-name>] - <date>` and start a fresh `[Unreleased]`.

If you're not sure whether to add an entry, add one. Too granular is better than missing.

---

## [Unreleased]

### Added
- `.claude/AUDIT-FINDINGS-CLAUDEMD.md` — Phase 3a audit of root `CLAUDE.md` against community best practices + Phase 0 inventory (75-section sign-off table + proposed skeleton + open questions). Commit `0c11cd43`.
- `.claude/archive/2026-05-17/CLAUDE.md` — preserved copy of the 1190-line OLD root `CLAUDE.md` before Phase 3b rewrite (reversibility per NF-1).

### Changed
- **Root `CLAUDE.md` rewritten** from 1190 → 264 lines (78% reduction) per Phase 3b. Applies community best practices: project-specific operational rules only; tutorials/marketing/long reference tables moved out; pointer-heavy structure. User-locked decisions: §1 identity updated to "enterprise AI-directed legal operations intelligence platform"; §11 System Entry Points + §12 Context Layer Hierarchy kept inline (user judgment); §13 Knowledge Repository section added pointing at `spaarke/knowledge/` + `researcher` subagent for rapidly-evolving Microsoft platform topics; Rigor Level template kept inline; Hooks: Current Guidance compressed to one paragraph.
- 5 internal contradictions resolved in the rewrite (Hooks System vs Current Guidance; trigger phrases in 2 places; Before-Starting-Work vs Working-Checklist; etc.).

### Removed
- The 22 extract-candidate sections totaling ~720 lines from old `CLAUDE.md`. Content remains preserved in `.claude/archive/2026-05-17/CLAUDE.md`. Topics removed: detailed Adaptive Thinking tutorial, Permission Modes tutorial, Hooks System tutorial, Headless Mode, Agent Teams (experimental), Component Skills note (now in `.claude/skills/INDEX.md`), Trigger Phrases table, Slash Commands table, Coding Standards code samples (in `docs/standards/`), Repository Structure tree (in `README.md`), ADR summary table (in `.claude/adr/INDEX.md`), Quality Gates with Hooks (feature not configured), and dated/duplicate sections.

### Fixed
- N/A — Phase 3a/3b are restructuring; no behavioral fixes in this scope.

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

> Entry will be promoted from `[Unreleased]` when the project's PR #294 merges. The deliverables below are the planned set.

### Added
- `.claude/agents/researcher.md` — Opus, effort: high researcher subagent for deep-dive Microsoft platform investigation; accumulates findings via project memory (`MEMORY.md`). Per design.md Directive 1. (Task 010)
- `.claude/skills/_template/SKILL.md` — canonical skill scaffold enforcing the 7 best practices; new skills clone this; existing skills are measured against it during Phase 2a audit. (Task 011)
- `.claude/CHANGELOG.md` — this file. Forward-only convention. (Task 012)
- `.claude/FAILURE-MODES.md` — repo-level catalog of cross-cutting failure patterns. 4 inaugural entries derived from 2026-05-14 incidents. (Task 013)
- `.claude/archive/` directory with date-organized subdirectory convention; reversibility-first removal pattern. (Task 014)
- `scripts/quality/Validate-SkillReferences.ps1` — Light reference check across all 49 skills (file paths, URLs, skill names). Runs in CI; <10s. (Task 065)
- `scripts/quality/Find-SkillReferenceDrift.ps1` — 7-surface drift detector; catches broken refs after rename/split/merge. (Task 066)

### Changed
- Root `CLAUDE.md` rewritten to the tiered target (<200 lines). Reference content moved to subdirectories. The pre-rewrite version is preserved in `.claude/archive/2026-05-14/CLAUDE.md`. (Phase 3b deliverable)
- Multiple skills refined per `.claude/AUDIT-FINDINGS-SKILLS.md`. Specific refactors listed under each skill in the per-skill section of the audit findings. (Phase 2b deliverable)

### Removed
- Skills audit-recommended-and-approved for removal (specific list determined at Human Gate 1). Folders archived to `.claude/archive/2026-05-14/skills/<name>/`, not deleted from disk. (Phase 2b deliverable)

### Fixed
- N/A — Phase 0 inventory surfaced existing issues (5 failing workflows, 3 PCFs with wrong `build:prod`, etc.) but their fixes are in separate scope from this project.

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 012). See [.claude/archive/README.md](archive/README.md) for the reversibility convention referenced above.*
