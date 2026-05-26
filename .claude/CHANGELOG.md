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

### Changed
- **`code-review` + `adr-check` now enforce CLAUDE.md §10 BFF Hygiene + `bff-extensions.md`** — closes the gap where the binding §10 rule was loaded as context but never explicitly checked. `adr-check` Step 2's quick-reference table adds ADR-013 (refined 2026-05-20); new Step 2.5 conditionally loads `bff-extensions.md` and applies its 5-rule pre-merge checklist when changed files touch `Sprk.Bff.Api/`, `Spaarke.Core/`, or `Spaarke.Dataverse/`. `code-review` Step 6 adds ADR-013 to its CRITICAL ADRs list; new Step 6.5 runs the same §10 checklist with explicit severity assignment (missing Placement Justification → Critical; new direct CRUD→AI dep → Critical; new HIGH-severity CVE → Critical). Both edits cite `bff-extensions.md` as the single source of truth — zero duplication of rule content.

### Added
- `.claude/AUDIT-FINDINGS-CLAUDEMD.md` — Phase 3a audit of root `CLAUDE.md` against community best practices + Phase 0 inventory (75-section sign-off table + proposed skeleton + open questions). Commit `0c11cd43`.
- `.claude/archive/2026-05-17/CLAUDE.md` — preserved copy of the 1190-line OLD root `CLAUDE.md` before Phase 3b rewrite (reversibility per NF-1).
- **Auth v2 pre-flight** — STOP banners on 5 partially-superseded docs (`.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/auth/token-caching.md`, `.claude/constraints/auth.md`, `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`, `docs/architecture/sdap-auth-patterns.md`) + full-deprecation banners on 2 DEPRECATED-* files. Each banner names what stays canonical (INV-1..INV-7, server-side OBO, `buildBffApiUrl()`, etc.). PF-4..PF-10. Commit `281f7210`.
- **Auth v2 pre-flight** — Pointer row in root `CLAUDE.md` §15 directing all agents (any worktree) to `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` as the active auth v2 design until ADR-027 ships. PF-12. Commit `5b04b6ff`.

### Changed
- **Root `CLAUDE.md` rewritten** from 1190 → 264 lines (78% reduction) per Phase 3b. Applies community best practices: project-specific operational rules only; tutorials/marketing/long reference tables moved out; pointer-heavy structure. User-locked decisions: §1 identity updated to "enterprise AI-directed legal operations intelligence platform"; §11 System Entry Points + §12 Context Layer Hierarchy kept inline (user judgment); §13 Knowledge Repository section added pointing at `spaarke/knowledge/` + `researcher` subagent for rapidly-evolving Microsoft platform topics; Rigor Level template kept inline; Hooks: Current Guidance compressed to one paragraph.
- 5 internal contradictions resolved in the rewrite (Hooks System vs Current Guidance; trigger phrases in 2 places; Before-Starting-Work vs Working-Checklist; etc.).
- **Auth v2 pre-flight** — 11 in-scope references updated to point at the new `DEPRECATED-*` filenames with "⛔ DEPRECATED — superseded by Spaarke Auth v2" markers: `.claude/patterns/auth/INDEX.md`, `.claude/patterns/INDEX.md`, `.claude/constraints/auth.md`, `.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/webresource/{code-page-wizard-wrapper.md, full-page-custom-page.md}`, `.claude/skills/code-page-deploy/SKILL.md`, `docs/architecture/sdap-auth-patterns.md`, `CROSS-REFERENCE-MAP.md`, `src/solutions/SpaarkeAi/src/App.tsx`, `src/solutions/Reporting/{main.tsx, services/authInit.ts, config/runtimeConfig.ts, config/reportingConfig.ts}`. Historical `projects/*` references, `.claude/archive/`, and the audit doc's rename-action narrative left intentionally unchanged. PF-3. Commit `c2198007`.

### Deprecated
- **Auth v2 pre-flight** — Two fully-superseded auth pattern docs renamed with `DEPRECATED-` prefix so the filename itself is a stop signal in Grep/Glob output:
  - `.claude/patterns/auth/msal-client.md` → `.claude/patterns/auth/DEPRECATED-msal-client.md`
  - `.claude/patterns/auth/spaarke-auth-initialization.md` → `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md`
  Both files will be removed when v2 ships (Workstream F4, task 094). PF-1, PF-2. Commit `c2198007`.

### Removed
- The 22 extract-candidate sections totaling ~720 lines from old `CLAUDE.md`. Content remains preserved in `.claude/archive/2026-05-17/CLAUDE.md`. Topics removed: detailed Adaptive Thinking tutorial, Permission Modes tutorial, Hooks System tutorial, Headless Mode, Agent Teams (experimental), Component Skills note (now in `.claude/skills/INDEX.md`), Trigger Phrases table, Slash Commands table, Coding Standards code samples (in `docs/standards/`), Repository Structure tree (in `README.md`), ADR summary table (in `.claude/adr/INDEX.md`), Quality Gates with Hooks (feature not configured), and dated/duplicate sections.

### Fixed
- N/A — Phase 3a/3b are restructuring; no behavioral fixes in this scope.

### Verified
- **Auth v2 pre-flight** — `projects/spaarke-auth-v2-and-hardening/CLAUDE.md` "🚨 ACTIVE AUTH V2 REFACTOR — DO NOT REGRESS" section cross-checked against audit §8.2 Layer 3 (PF-11) requirements. All MUST/MUST NOT bullets present plus extras (/debug endpoint ban, plain-text secret ban, INV-1..INV-8 preservation). No edits required. PF-11. Commit `f58317b0`.

### Retirement note
- All "Auth v2 pre-flight" entries above (PF-1..PF-13) are transitional. They will be retired during Workstream F (Engineering canonical docs): F1 ships ADR-027, F2 partial-rewrites `spaarke-sso-binding.md`, F3 ships `docs/guides/auth-deployment-setup.md`, F4 deletes the `DEPRECATED-*` files and removes the STOP banners + project CLAUDE.md prohibition + root CLAUDE.md pointer row. See `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` §8.4–§8.5.

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
